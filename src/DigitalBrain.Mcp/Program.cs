using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
ConfigureOrleansClient(builder);
builder.AddKeyedAzureBlobServiceClient("features", settings => settings.DisableTracing = true);
var configuredProfile = builder.Configuration["DigitalBrain:Profile"];
if (string.IsNullOrWhiteSpace(configuredProfile))
    throw new InvalidOperationException("DigitalBrain:Profile must be supplied by the AppHost or deployment configuration.");
var profileText = configuredProfile;
if (!Enum.TryParse<RuntimeProfile>(profileText, true, out var profile))
    throw new InvalidOperationException($"Unknown runtime profile '{profileText}'.");
builder.Services.AddMcpServer().WithHttpTransport().WithTools<McpConversationTools>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<McpAuthority>();
builder.Services.AddSingleton(TimeProvider.System);
var mcpAudience = SessionAudiences.RequireFixedMcp(builder.Configuration["DigitalBrain:Runtime:Mcp:Audience"]);
builder.Services.AddSingleton<ConversationStateClient>();
builder.Services.AddSingleton<McpInoCommandHandler>();
builder.Services.AddSingleton<FeatureArtifactPublisher>();
builder.Services.AddSingleton<IFeatureArtifactCatalog>(services => services.GetRequiredService<FeatureArtifactPublisher>());
builder.Services.AddSingleton<FeatureBuildEndpoint>();
builder.Services.AddSingleton<IFeatureBuildEndpoint>(services => services.GetRequiredService<FeatureBuildEndpoint>());
builder.Services.AddSingleton<FeatureLifecycleRail>();
builder.Services.AddSingleton<IFeatureLifecycleRail>(services => services.GetRequiredService<FeatureLifecycleRail>());
builder.Services.AddSingleton<DigitalBrainQueryService>();
builder.Services.AddSingleton<FeatureSuggestionService>();
builder.Services.AddFeatureCapabilityCatalog();
builder.Services.AddSingleton<FeatureAuthoringService>();
builder.Services.AddSingleton(AuthorizationFlowProxyOptions.FromConfiguration(builder.Configuration, profile));
builder.Services.AddHttpClient<AuthorizationFlowStartProxy>().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
var sessionKeyText = builder.Configuration["DigitalBrain:Auth:SessionSigningKey"] ?? Environment.GetEnvironmentVariable("DigitalBrain__Auth__SessionSigningKey");
if (string.IsNullOrWhiteSpace(sessionKeyText)) throw new InvalidOperationException("A session signing key is required for HTTP MCP.");
byte[] sessionSigningKey;
try { sessionSigningKey = Convert.FromBase64String(sessionKeyText); }
catch (FormatException exception) { throw new InvalidOperationException("The session signing key must be valid base64.", exception); }
builder.Services.AddSingleton(new SessionTokenService(sessionSigningKey, TimeProvider.System));
builder.Services.AddSingleton<RuntimeSessionAuthority>();
builder.Services.AddSingleton<RuntimeRequestAuthenticator>();
builder.Services.AddSingleton(RuntimeTransportBoundaryOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(sp => new McpRequestGuard(new McpTransportPolicy(
    mcpAudience,
    new HashSet<string>((builder.Configuration["DigitalBrain:Runtime:Mcp:AllowedOrigins"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal),
    int.TryParse(builder.Configuration["DigitalBrain:Runtime:Mcp:MaxBodyBytes"], out var body) ? body : 1_048_576,
    int.TryParse(builder.Configuration["DigitalBrain:Runtime:Mcp:MaxConcurrentRequests"], out var concurrent) ? concurrent : 8,
    int.TryParse(builder.Configuration["DigitalBrain:Runtime:Mcp:RequestsPerMinute"], out var rate) ? rate : 120)));
builder.Services.AddUiTransport(builder.Configuration, builder.Environment, profile);
var app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<RuntimeTransportBoundary>();
app.MapUiTransport();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/mcp"))
    {
        await next();
        return;
    }
    var principal = await context.RequestServices.GetRequiredService<RuntimeRequestAuthenticator>().AuthenticateMcpAsync(context, context.RequestAborted)
        .ConfigureAwait(false);
    if (principal is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
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
});
app.MapMcp("/mcp");
app.MapGet("/oauth/start/{provider}", (string provider, HttpRequest request, AuthorizationFlowStartProxy proxy, CancellationToken cancellationToken) =>
    proxy.StartAsync(provider, request, cancellationToken));
app.MapDefaultEndpoints();
await app.RunAsync();
static void ConfigureOrleansClient(IHostApplicationBuilder builder)
{
    builder.Services.AddSingleton<IClientConnectionRetryFilter, BoundedOrleansClientConnectionRetryFilter>();
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
