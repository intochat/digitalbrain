// DigitalBrain.Mcp - standalone MCP server for DigitalBrain.
// An Orleans CLIENT that exposes cluster interactions as MCP tools (DigitalBrain.Mcp.Tools).
// Requires the kernel cluster (storage + Ollama) to be running - the tools operate on real grains, so there is
// no degraded no-cluster mode (fail-fast). The authenticated HTTP transport is the only production surface.

using System.Text.Json;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;


var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ConfigureOrleansClient(builder);

// HTTP is the authoritative MCP surface; no compatibility transport is registered.
var mcp = builder.Services.AddMcpServer().WithHttpTransport();
mcp.WithTools<V2McpTools>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IV2TelemetrySink, V2TelemetryBuffer>();
var profileText = builder.Configuration["DigitalBrain:Profile"] ?? "Development";
if (!Enum.TryParse<V2RuntimeProfile>(profileText, true, out var profile))
    throw new InvalidOperationException($"Unknown V2 runtime profile '{profileText}'.");
var mcpAudience = V2SessionAudiences.RequireFixedMcp(builder.Configuration["DigitalBrain:V2:Mcp:Audience"]);
var manifest = V2CapabilityManifests.For(profile);
builder.Services.AddSingleton(new V2SchemaRegistry([
    new V2SchemaDescriptor("digitalbrain.v2.command-envelope", 2, "Operational", true),
        new V2SchemaDescriptor("digitalbrain.v2.event-envelope", 2, "Operational", true),
        new V2SchemaDescriptor("digitalbrain.v2.workflow-persisted-state", 2, "Operational", true)]));
var operationStorePath = builder.Configuration["DigitalBrain:V2:OperationStorePath"];
var sessionStorePath = builder.Configuration["DigitalBrain:V2:SessionStorePath"];
var projectionPath = builder.Configuration["DigitalBrain:V2:ProjectionStorePath"];
if (profile == V2RuntimeProfile.Production &&
    (string.IsNullOrWhiteSpace(operationStorePath) || string.IsNullOrWhiteSpace(sessionStorePath) || string.IsNullOrWhiteSpace(projectionPath)))
    throw new InvalidOperationException("Production V2 requires durable operation, session, and projection stores; in-memory fallbacks are disabled.");
var v2Capabilities = manifest.Enabled
    .Where(x => x is "brain.read" or "brain.act" or "brain.approve" or "brain.admin")
    .Select(x => new V2Capability(x, 2, true, x is not "brain.read"))
    .ToList();
if (!string.Equals(builder.Configuration["DigitalBrain:Mcp:EnableAdmin"], "true", StringComparison.OrdinalIgnoreCase))
    v2Capabilities.RemoveAll(x => x.Id == "brain.admin");
builder.Services.AddSingleton(new V2ApplicationService(
    capabilities: v2Capabilities,
    storagePath: operationStorePath));
// The dispatcher is application-owned; handlers are intentionally supplied by the
// V2 composition and an empty handler set fails closed to ManualIntervention.
builder.Services.AddSingleton<IV2CommandHandler, V2McpEffectCommandHandler>();
builder.Services.AddSingleton<IV2EffectWorkerPort, OrleansClientV2EffectWorkerPort>();
var inoEffectStorePath = builder.Configuration["DigitalBrain:V2:InoEffectStorePath"];
if (string.IsNullOrWhiteSpace(inoEffectStorePath) && !string.IsNullOrWhiteSpace(operationStorePath)) inoEffectStorePath = operationStorePath + ".ino-effects";
if (profile == V2RuntimeProfile.Production && string.IsNullOrWhiteSpace(inoEffectStorePath)) throw new InvalidOperationException("Production V2 requires a durable INO effect store.");
builder.Services.AddSingleton(new V2InoEffectStore(inoEffectStorePath));
builder.Services.AddSingleton<IV2InoConversationStore>(serviceProvider => serviceProvider.GetRequiredService<V2InoEffectStore>());
builder.Services.AddSingleton<IV2ContextAssembler, V2McpConversationContextAssembler>();
builder.Services.AddSingleton<IV2IntentCapabilityPlanner, V2McpIntegrationPlanner>();
builder.Services.AddSingleton<IV2ModelRouter, V2McpConversationModelRouter>();
builder.Services.AddSingleton<IV2McpIntegrationToolGateway, V2McpIntegrationToolGateway>();
builder.Services.AddSingleton<IV2AuthorizedToolCatalog, V2McpAuthorizedToolCatalog>();
builder.Services.AddSingleton<IV2ResponseSurfaceComposer, V2McpResponseComposer>();
builder.Services.AddSingleton<V2ConversationOwner>();
builder.Services.AddSingleton<IV2CommandHandler, V2McpInoCommandHandler>();
builder.Services.AddSingleton<V2CommandDispatcher>();
builder.Services.AddHostedService<V2CommandExecutionWorker>();
var sessionKeyText = builder.Configuration["DigitalBrain:Auth:SessionSigningKey"] ?? Environment.GetEnvironmentVariable("DigitalBrain__Auth__SessionSigningKey");
if (string.IsNullOrWhiteSpace(sessionKeyText)) throw new InvalidOperationException("V2 session signing key is required for HTTP MCP.");
builder.Services.AddSingleton(new V2SessionTokenService(Convert.FromBase64String(sessionKeyText)));
if (!string.IsNullOrWhiteSpace(sessionStorePath))
    builder.Services.AddSingleton<IV2SessionManager>(sp => new FileV2SessionManager(sp.GetRequiredService<V2SessionTokenService>(), sessionStorePath));
else
    builder.Services.AddSingleton<IV2SessionManager, V2SessionManager>();
if (!string.IsNullOrWhiteSpace(projectionPath))
{
    builder.Services.AddSingleton<FileV2ProjectionQueryStore>(_ => new FileV2ProjectionQueryStore(projectionPath));
    builder.Services.AddSingleton<IV2ProjectionQueryPort>(sp => sp.GetRequiredService<FileV2ProjectionQueryStore>());
}
else
{
    builder.Services.AddSingleton<InMemoryV2ProjectionQueryStore>();
    builder.Services.AddSingleton<IV2ProjectionQueryPort>(sp => sp.GetRequiredService<InMemoryV2ProjectionQueryStore>());
}
builder.Services.AddSingleton(sp => new V2McpRequestGuard(new V2McpTransportPolicy(
    mcpAudience,
    new HashSet<string>((builder.Configuration["DigitalBrain:V2:Mcp:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal),
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:MaxBodyBytes"], out var body) ? body : 1_048_576,
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:MaxConcurrentRequests"], out var concurrent) ? concurrent : 8,
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:RequestsPerMinute"], out var rate) ? rate : 120)));
builder.Services.AddV2UiTransport(builder.Configuration, builder.Environment, profile);

var app = builder.Build();
app.MapV2UiTransport();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp") && !TryGetV2Context(context, out _))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("V2 MCP authentication required.");
        return;
    }
    if (context.Request.Path.StartsWithSegments("/mcp") && TryGetV2Context(context, out var principal))
    {
        var guard = context.RequestServices.GetRequiredService<V2McpRequestGuard>();
        if (!guard.TryBegin(V2RequestScope.Id(principal), context.Request.Headers.Origin, context.Request.Headers["X-V2-Audience"].ToString(), (int)(context.Request.ContentLength ?? 0), out var lease))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        using (lease) await next();
        return;
    }
    await next();
});
app.MapMcp("/mcp");
app.MapGet("/v2/capabilities", async (HttpContext context, V2ApplicationService service) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await service.GetCapabilitiesAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/operations", async (HttpContext context, V2ApplicationService service) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await service.GetOperationsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/operations/{operationId}", async (HttpContext context, string operationId, V2ApplicationService service) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    var operation = await service.GetOperationAsync(principal, operationId, context.RequestAborted);
    return operation is null ? Results.NotFound() : Results.Ok(operation);
});
app.MapGet("/v2/timeline", async (HttpContext context, IV2ProjectionQueryPort store) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.TimelineAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapPost("/v2/ino/commands", async (HttpContext context, V2ApplicationService service, JsonElement body) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    if (!V2McpInoCommandHandler.TryGetPrompt(body, out _)) return Results.BadRequest(new V2McpError("invalid_ino_command", "INO commands accept only a prompt.", principal.CorrelationId));
    var commandId = context.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 256) return Results.BadRequest(new V2McpError("invalid_idempotency", "Idempotency-Key is required.", principal.CorrelationId));
    try
    {
        var operation = await service.SubmitAsync(principal,
            new V2CommandEnvelope(V2McpInoCommandHandler.CommandType, 2, commandId, principal, body.Clone()),
            context.RequestAborted);
        return Results.Accepted($"/v2/operations/{operation.OperationId}", operation);
    }
    catch (V2IdempotencyConflictException)
    {
        return Results.Conflict(new V2McpError("idempotency_conflict",
            "The idempotency key was already used for different input.", principal.CorrelationId));
    }
});
app.MapGet("/v2/workflows", async (HttpContext context, IV2ProjectionQueryPort store) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.WorkflowsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/connectors", async (HttpContext context, IV2ProjectionQueryPort store) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.ConnectorsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapPost("/v2/commands", async (HttpContext context, V2ApplicationService service, JsonElement body) =>
{
    if (!TryGetV2Context(context, out var principal)) return Results.Unauthorized();
    var type = body.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
    var commandId = body.TryGetProperty("commandId", out var idValue) ? idValue.GetString() : null;
    if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(commandId)) return Results.BadRequest(new V2McpError("invalid_command", "type and commandId are required", principal.CorrelationId));
    var envelope = new V2CommandEnvelope(type!, 2, commandId!, principal, body.Clone());
    try
    {
        var operation = await service.SubmitAsync(principal, envelope, context.RequestAborted);
        return Results.Accepted($"/v2/operations/{operation.OperationId}", operation);
    }
    catch (V2IdempotencyConflictException)
    {
        return Results.Conflict(new V2McpError("idempotency_conflict",
            "The idempotency key was already used for different input.", principal.CorrelationId));
    }
});
app.MapPost("/v2/session/refresh", (IV2SessionManager sessions, V2SessionRefreshRequest request) =>
{
    return sessions.TryRefresh(request.RefreshToken, TimeSpan.FromMinutes(15), V2SessionAudiences.Mcp, out var pair)
        ? Results.Ok(pair)
        : Results.Unauthorized();
});
app.MapPost("/v2/session/logout", (IV2SessionManager sessions, V2SessionRefreshRequest request) =>
    sessions.Revoke(request.RefreshToken, V2SessionAudiences.Mcp) ? Results.NoContent() : Results.NotFound());
app.MapDefaultEndpoints();
await app.RunAsync();

static bool TryGetV2Context(HttpContext context, out V2RequestContext requestContext)
{
    requestContext = default!;
    if (!context.Request.Headers.TryGetValue("Authorization", out var auth) || auth.Count != 1 || !auth[0]!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    var tokens = context.RequestServices.GetRequiredService<V2SessionTokenService>();
    return tokens.TryValidate(auth[0]![7..].Trim(), V2SessionAudiences.Mcp, out requestContext);
}
static int ParseLimit(HttpContext context) => int.TryParse(context.Request.Query["limit"], out var value) ? Math.Clamp(value, 1, 100) : 50;

static void ConfigureOrleansClient(IHostApplicationBuilder builder)
{
    // Orleans client clustering: Aspire injects the provider type. Azure Table in cloud, Redis locally.
    var clusteringProvider = Environment.GetEnvironmentVariable("Orleans__Clustering__ProviderType");
    if (string.Equals(clusteringProvider, "AzureTableStorage", StringComparison.OrdinalIgnoreCase))
    {
        var clusteringServiceKey = Environment.GetEnvironmentVariable("Orleans__Clustering__ServiceKey") ?? "clustering";
        builder.AddKeyedAzureTableServiceClient(clusteringServiceKey);
    }
    else
    {
        builder.AddKeyedRedisClient("redis");
    }

    builder.UseOrleansClient();
}
