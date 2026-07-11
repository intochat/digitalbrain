// DigitalBrain.Mcp - standalone MCP server for DigitalBrain.
// An Orleans CLIENT that exposes cluster interactions as MCP tools (DigitalBrain.Mcp.Tools).
// Requires the kernel cluster (storage + Ollama) to be running - the tools operate on real grains, so there is
// no degraded no-cluster mode (fail-fast). The authenticated HTTP transport is the only production surface.

using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;


var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ConfigureOrleansClient(builder);

var configuredProfile = builder.Configuration["DigitalBrain:Profile"];
if (string.IsNullOrWhiteSpace(configuredProfile))
    throw new InvalidOperationException("DigitalBrain:Profile must be supplied by the AppHost or deployment configuration.");
var profileText = configuredProfile;
if (!Enum.TryParse<RuntimeProfile>(profileText, true, out var profile))
    throw new InvalidOperationException($"Unknown runtime profile '{profileText}'.");
var runtimePolicy = RuntimePolicy.Resolve(
    profile,
    mutationsRequested: string.Equals(builder.Configuration["DigitalBrain:Mcp:EnableMutations"], "true", StringComparison.OrdinalIgnoreCase),
    adminRequested: string.Equals(builder.Configuration["DigitalBrain:Mcp:EnableAdmin"], "true", StringComparison.OrdinalIgnoreCase));
var mutationsEnabled = runtimePolicy.MutationsEnabled;
var adminEnabled = runtimePolicy.AdminEnabled;

// HTTP is the authoritative MCP surface; no compatibility transport is registered.
var mcp = builder.Services.AddMcpServer().WithHttpTransport();
mcp.WithTools<McpReadTools>();
if (mutationsEnabled) mcp.WithTools<McpMutationTools>();
if (adminEnabled) mcp.WithTools<McpAdminTools>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<McpAuthority>();
builder.Services.AddSingleton<ITelemetrySink, TelemetryBuffer>();
var mcpAudience = SessionAudiences.RequireFixedMcp(builder.Configuration["DigitalBrain:V2:Mcp:Audience"]);
builder.Services.AddSingleton(new SchemaRegistry([
    new SchemaDescriptor("digitalbrain.v2.command-envelope", 2, "Operational", true),
    new SchemaDescriptor("digitalbrain.v2.event-envelope", 2, "Operational", true),
    new SchemaDescriptor("digitalbrain.v2.workflow-persisted-state", 2, "Operational", true)]));
var operationStorePath = builder.Configuration["DigitalBrain:V2:OperationStorePath"];
var sessionStorePath = builder.Configuration["DigitalBrain:V2:SessionStorePath"];
var projectionPath = builder.Configuration["DigitalBrain:V2:ProjectionStorePath"];
if (profile == RuntimeProfile.Production &&
    (string.IsNullOrWhiteSpace(operationStorePath) || string.IsNullOrWhiteSpace(sessionStorePath) || string.IsNullOrWhiteSpace(projectionPath)))
    throw new InvalidOperationException("Production runtime requires durable operation, session, and projection stores; in-memory fallbacks are disabled.");
byte[]? journalIntegrityKey = null;
if (!string.IsNullOrWhiteSpace(operationStorePath) || !string.IsNullOrWhiteSpace(sessionStorePath))
{
    var journalKeyText = builder.Configuration["DigitalBrain:V2:JournalIntegrityKey"] ??
                         Environment.GetEnvironmentVariable("DigitalBrain__V2__JournalIntegrityKey");
    if (string.IsNullOrWhiteSpace(journalKeyText))
        throw new InvalidOperationException("A stable journal integrity key is required for durable operation and session stores.");
    try { journalIntegrityKey = Convert.FromBase64String(journalKeyText); }
    catch (FormatException exception) { throw new InvalidOperationException("The journal integrity key must be valid base64.", exception); }
    if (journalIntegrityKey.Length < 32)
        throw new InvalidOperationException("The journal integrity key must contain at least 256 bits.");
}
builder.Services.AddSingleton(new ApplicationService(
    capabilities: runtimePolicy.McpCapabilities,
    storagePath: operationStorePath,
    journalIntegrityKey: journalIntegrityKey));
// The dispatcher is application-owned; handlers are intentionally supplied by the
// Runtime composition and an empty handler set fail closed to ManualIntervention.
builder.Services.AddSingleton<ICommandHandler, McpEffectCommandHandler>();
builder.Services.AddSingleton<IEffectWorkerPort, OrleansClientEffectWorkerPort>();
var inoEffectStorePath = builder.Configuration["DigitalBrain:V2:InoEffectStorePath"];
if (string.IsNullOrWhiteSpace(inoEffectStorePath) && !string.IsNullOrWhiteSpace(operationStorePath)) inoEffectStorePath = operationStorePath + ".ino-effects";
if (profile == RuntimeProfile.Production && string.IsNullOrWhiteSpace(inoEffectStorePath)) throw new InvalidOperationException("Production runtime requires a durable INO effect store.");
var toolActionPolicy = new ToolActionPolicy(builder.Configuration["DigitalBrain:Salesforce:RedirectUri"]);
builder.Services.AddSingleton(toolActionPolicy);
builder.Services.AddSingleton(new InoEffectStore(inoEffectStorePath, toolActionPolicy));
builder.Services.AddSingleton<IInoConversationStore>(serviceProvider => serviceProvider.GetRequiredService<InoEffectStore>());
builder.Services.AddSingleton<IContextAssembler, McpConversationContextAssembler>();
builder.Services.AddSingleton<ISemanticIntentResolver, McpSemanticIntentResolver>();
builder.Services.AddSingleton<IIntentCapabilityPlanner, McpIntegrationPlanner>();
builder.Services.AddSingleton<IModelRouter, McpConversationModelRouter>();
builder.Services.AddSingleton<IMcpIntegrationToolGateway, McpIntegrationToolGateway>();
builder.Services.AddSingleton<IAuthorizedToolCatalog, McpAuthorizedToolCatalog>();
builder.Services.AddSingleton<IResponseSurfaceComposer, McpResponseComposer>();
builder.Services.AddSingleton<ConversationOwner>();
builder.Services.AddSingleton<ICommandHandler, McpInoCommandHandler>();
builder.Services.AddSingleton<CommandDispatcher>();
builder.Services.AddHostedService<CommandExecutionWorker>();
var sessionKeyText = builder.Configuration["DigitalBrain:Auth:SessionSigningKey"] ?? Environment.GetEnvironmentVariable("DigitalBrain__Auth__SessionSigningKey");
if (string.IsNullOrWhiteSpace(sessionKeyText)) throw new InvalidOperationException("A session signing key is required for HTTP MCP.");
builder.Services.AddSingleton(new SessionTokenService(Convert.FromBase64String(sessionKeyText)));
if (!string.IsNullOrWhiteSpace(sessionStorePath))
    builder.Services.AddSingleton<ISessionManager>(sp => new FileSessionManager(
        sp.GetRequiredService<SessionTokenService>(),
        sessionStorePath,
        journalIntegrityKey: journalIntegrityKey));
else
    builder.Services.AddSingleton<ISessionManager, SessionManager>();
if (!string.IsNullOrWhiteSpace(projectionPath))
{
    builder.Services.AddSingleton<FileProjectionQueryStore>(_ => new FileProjectionQueryStore(projectionPath));
    builder.Services.AddSingleton<IProjectionQueryPort>(sp => sp.GetRequiredService<FileProjectionQueryStore>());
}
else
{
    builder.Services.AddSingleton<InMemoryProjectionQueryStore>();
    builder.Services.AddSingleton<IProjectionQueryPort>(sp => sp.GetRequiredService<InMemoryProjectionQueryStore>());
}
builder.Services.AddSingleton(sp => new McpRequestGuard(new McpTransportPolicy(
    mcpAudience,
    new HashSet<string>((builder.Configuration["DigitalBrain:V2:Mcp:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal),
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:MaxBodyBytes"], out var body) ? body : 1_048_576,
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:MaxConcurrentRequests"], out var concurrent) ? concurrent : 8,
    int.TryParse(builder.Configuration["DigitalBrain:V2:Mcp:RequestsPerMinute"], out var rate) ? rate : 120)));
builder.Services.AddUiTransport(builder.Configuration, builder.Environment, profile);

var app = builder.Build();
app.MapUiTransport();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp") && !TryGetContext(context, out _))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("MCP authentication required.");
        return;
    }
    if (context.Request.Path.StartsWithSegments("/mcp") && TryGetContext(context, out var principal))
    {
        var guard = context.RequestServices.GetRequiredService<McpRequestGuard>();
        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = guard.MaxBodyBytes;
        if (!guard.TryBegin(RequestScope.Id(principal), context.Request.Headers.Origin,
                context.Request.Headers["X-V2-Audience"].ToString(), context.Request.ContentLength, out var lease))
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
app.MapGet("/v2/capabilities", async (HttpContext context, ApplicationService service) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await service.GetCapabilitiesAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/operations", async (HttpContext context, ApplicationService service) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await service.GetOperationsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/operations/{operationId}", async (HttpContext context, string operationId, ApplicationService service) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    var operation = await service.GetOperationAsync(principal, operationId, context.RequestAborted);
    return operation is null ? Results.NotFound() : Results.Ok(operation);
});
app.MapGet("/v2/timeline", async (HttpContext context, IProjectionQueryPort store) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.TimelineAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
if (mutationsEnabled)
{
    app.MapPost("/v2/ino/commands", async (HttpContext context, ApplicationService service, JsonElement body) =>
    {
        if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
        if (!McpInoCommandHandler.TryGetPrompt(body, out _)) return Results.BadRequest(new McpError("invalid_ino_command", "INO commands accept only a prompt.", principal.CorrelationId));
        var commandId = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 256) return Results.BadRequest(new McpError("invalid_idempotency", "Idempotency-Key is required.", principal.CorrelationId));
        try
        {
            var operation = await service.SubmitAsync(principal,
                new CommandEnvelope(McpInoCommandHandler.CommandType, 2, commandId, principal, body.Clone()),
                context.RequestAborted);
            return Results.Accepted($"/v2/operations/{operation.OperationId}", operation);
        }
        catch (IdempotencyConflictException)
        {
            return Results.Conflict(new McpError("idempotency_conflict",
                "The idempotency key was already used for different input.", principal.CorrelationId));
        }
    });
}
app.MapGet("/v2/workflows", async (HttpContext context, IProjectionQueryPort store) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.WorkflowsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
app.MapGet("/v2/connectors", async (HttpContext context, IProjectionQueryPort store) =>
{
    if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
    return Results.Ok(await store.ConnectorsAsync(principal, context.Request.Query["cursor"], ParseLimit(context), context.RequestAborted));
});
if (mutationsEnabled)
{
    app.MapPost("/v2/commands", async (HttpContext context, ApplicationService service, JsonElement body) =>
    {
        if (!TryGetContext(context, out var principal)) return Results.Unauthorized();
        var type = body.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
        var commandId = body.TryGetProperty("commandId", out var idValue) ? idValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(commandId)) return Results.BadRequest(new McpError("invalid_command", "type and commandId are required", principal.CorrelationId));
        var envelope = new CommandEnvelope(type!, 2, commandId!, principal, body.Clone());
        try
        {
            var operation = await service.SubmitAsync(principal, envelope, context.RequestAborted);
            return Results.Accepted($"/v2/operations/{operation.OperationId}", operation);
        }
        catch (IdempotencyConflictException)
        {
            return Results.Conflict(new McpError("idempotency_conflict",
                "The idempotency key was already used for different input.", principal.CorrelationId));
        }
    });
}
app.MapPost("/v2/session/refresh", (ISessionManager sessions, SessionRefreshRequest request) =>
{
    return sessions.TryRefresh(request.RefreshToken, TimeSpan.FromMinutes(15), SessionAudiences.Mcp, out var pair)
        ? Results.Ok(pair)
        : Results.Unauthorized();
});
app.MapPost("/v2/session/logout", (ISessionManager sessions, SessionRefreshRequest request) =>
    sessions.Revoke(request.RefreshToken, SessionAudiences.Mcp) ? Results.NoContent() : Results.NotFound());
app.MapDefaultEndpoints();
await app.RunAsync();

static bool TryGetContext(HttpContext context, out RuntimeRequestContext requestContext)
{
    requestContext = default!;
    if (!context.Request.Headers.TryGetValue("Authorization", out var auth) || auth.Count != 1 || !auth[0]!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    var tokens = context.RequestServices.GetRequiredService<SessionTokenService>();
    return tokens.TryValidate(auth[0]![7..].Trim(), SessionAudiences.Mcp, out requestContext);
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
