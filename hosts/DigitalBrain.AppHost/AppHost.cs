using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AppHost;
using AnthropicModels = DigitalBrain.Core.Models.Anthropic;
using GitHubModels = DigitalBrain.Core.Models.GitHub;
using OllamaModels = DigitalBrain.Core.Models.Ollama;
using OpenAIModels = DigitalBrain.Core.Models.OpenAI;

var builder = DistributedApplication.CreateBuilder(args);

// This is the authoritative local/test composition. Publish and deploy must choose an explicit profile.
var configuredProfile = builder.Configuration["DigitalBrain:Profile"];
var profile = configuredProfile ?? (builder.ExecutionContext.IsRunMode
    ? "Development"
    : throw new InvalidOperationException("DigitalBrain:Profile must be configured for publish and deploy."));
if (!IsKnownRuntimeProfile(profile))
    throw new InvalidOperationException($"Unknown runtime profile '{profile}'.");
var isRunMode = builder.ExecutionContext.IsRunMode;
var sessionSigningKey = AddRuntimeSecret(builder, "runtime-session-signing-key", isRunMode);
var runtimeStateKek = AddRuntimeSecret(builder, "runtime-state-kek-v1", isRunMode);
var runtimeStateSigningKey = AddRuntimeSecret(builder, "runtime-state-signing-key", isRunMode);
var featureHostInternalToken = AddRuntimeSecret(builder, "feature-host-internal-token", isRunMode);
var enableDevFlutter = isRunMode && IsLocalUiProfile(profile);
var uiOidcIssuer = builder.Configuration["DigitalBrain:Runtime:Ui:Oidc:Issuer"]?.Trim();
var uiOidcAudience = builder.Configuration["DigitalBrain:Runtime:Ui:Oidc:Audience"]?.Trim();
var hasAnyUiOidcConfiguration = !string.IsNullOrEmpty(uiOidcIssuer) || !string.IsNullOrEmpty(uiOidcAudience);
if (enableDevFlutter && hasAnyUiOidcConfiguration &&
    (string.IsNullOrEmpty(uiOidcIssuer) || string.IsNullOrEmpty(uiOidcAudience)))
    throw new InvalidOperationException(
        "DigitalBrain:Runtime:Ui:Oidc:Issuer and Audience must be configured together for the local browser UI.");
var flutterWebPort = ParseOptionalPort(builder.Configuration["DigitalBrain:Runtime:Ui:WebPort"]);
IResourceBuilder<ParameterResource>? uiBootstrapSecret = enableDevFlutter
    ? builder.AddParameter(
        "runtime-ui-bootstrap-secret",
        () => builder.Configuration["Parameters:runtime-ui-bootstrap-secret"] ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        secret: true)
    : null;

// Integrations stay thin here: INO, Gmail, and Salesforce are wired through the kernel;
// Flutter is wired only to MCP's authenticated UI boundary.

// Experiences emit UiSurface (AuthButtonSurface etc) for the Flutter runtime.
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    // --- Ollama (local first, 3060 Ti / 8GB VRAM) ---
    options
        .WithLLM<OllamaModels.Llama31_8B>().AsBalanced()
        .WithEmbedding<OllamaModels.MxbaiEmbedLarge>();

    // --- OpenAI ---
    // options
    //     .WithLLM<OpenAIModels.Gpt54Nano>().AsFast()
    //     .WithLLM<OpenAIModels.Gpt54Mini>().AsBalanced()
    //     .WithLLM<OpenAIModels.Gpt54>().AsReasoning()
    //     .WithEmbedding<OpenAIModels.TextEmbedding3Small>();

    // --- Anthropic (no embedding API; uses OpenAI embedding) ---
    // options
    //     .WithLLM<AnthropicModels.Claude45Haiku>().AsFast()
    //     .WithLLM<AnthropicModels.Sonnet46>().AsBalanced()
    //     .WithLLM<AnthropicModels.Opus46>().AsReasoning()
    //     .WithEmbedding<OpenAIModels.TextEmbedding3Small>();

    // --- GitHub Models (full tool calling, OpenAI-compatible endpoint) ---
    // options
    //     .WithLLM<GitHubModels.Gpt41Nano>().AsFast()
    //     .WithLLM<GitHubModels.Gpt41Mini>().AsBalanced()
    //     .WithLLM<GitHubModels.O4Mini>().AsReasoning()
    //     .WithEmbedding<GitHubModels.TextEmbedding3Small>();

});

var salesforceAppConfig = builder.AddSalesforceAppConfig();
var googleAppConfig = builder.AddGoogleAppConfig();

var kernel = builder.AddProject<Projects.DigitalBrain_RuntimeHost>("kernel");
ctx.ConfigureServer(kernel);
kernel.WithEnvironment("DigitalBrain__Profile", profile);
kernel.WithEnvironment("DigitalBrain__Tools__Enabled", "true");
kernel.WithEnvironment("DigitalBrain__Runtime__State__ActiveKekVersion", "1");
kernel.WithEnvironment("DigitalBrain__Runtime__State__Keks__1", runtimeStateKek);
kernel.WithEnvironment("DigitalBrain__Runtime__State__SigningKey", runtimeStateSigningKey);
kernel.WithEnvironment("DigitalBrain__FeatureHost__InternalToken", featureHostInternalToken);
kernel.WithEnvironment("DigitalBrain__Runtime__StorageNamespace",
    builder.Configuration["DigitalBrain:Runtime:StorageNamespace"] ?? DigitalBrainBuilderExtensions.DefaultRuntimeStorageNamespace);
kernel.WithSalesforceAppConfig(salesforceAppConfig);
kernel.WithGoogleAppConfig(googleAppConfig);

var featureHost = builder.AddProject<Projects.DigitalBrain_FeatureHost>("feature-host")
    .WithReference(ctx.FeatureArtifacts)
    .WithEnvironment("DigitalBrain__FeatureHost__InternalOrigin", kernel.GetEndpoint("web"))
    .WithEnvironment("DigitalBrain__FeatureHost__InternalToken", featureHostInternalToken)
    .WithReplicas(1);
ctx.ConfigureClient(featureHost);
featureHost.WaitFor(ctx.FeatureArtifacts);
featureHost.WaitFor(kernel);

builder.AddProject<Projects.DigitalBrain_FeatureBuilder>("feature-builder")
    .WithExplicitStart();

if (ctx.EnableMcp)
{
    // Dedicated single-replica HTTP MCP resource; avoids stateful MCP sessions going through the replicated kernel proxy.
    // The local launch profile pins ports that collide with the AppHost's proxy model.
    // Exclude it so Aspire owns randomized proxy and target ports in every isolated session.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp", launchProfileName: null)
        .WithEnvironment("DigitalBrain__Runtime__OAuth__InternalOrigin", kernel.GetEndpoint("web"))
        .WithEnvironment("DigitalBrain__Auth__SessionSigningKey", sessionSigningKey)
        .WithEnvironment("DigitalBrain__Profile", profile)
        .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", salesforceAppConfig.RedirectUri)
        .WithEnvironment("DigitalBrain__Runtime__Mcp__Audience", DigitalBrain.Core.Runtime.SessionAudiences.Mcp)
        .WithEnvironment("DigitalBrain__Runtime__Ui__Audience", DigitalBrain.Core.Runtime.SessionAudiences.Ui)
        .WithEndpoint(name: "http", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
        .WithHttpsEndpoint(name: "https", env: "ASPNETCORE_HTTPS_PORTS", isProxied: true)
        .AsHttp2Service()
        .WithEndpoint("https", endpoint => endpoint.Transport = "http2")
        .WithHttpHealthCheck(path: "/health", endpointName: "https")
        .WithReplicas(1);
    ctx.ConfigureClient(mcp);
    mcp.WaitFor(ctx.ConversationStateBlobs);
    mcp.WaitFor(ctx.SurfaceFeedStateBlobs);
    mcp.WaitFor(ctx.SessionStateBlobs);
    mcp.WaitFor(kernel);

    if (uiBootstrapSecret is not null)
    {
        // This is a local, scope-limited exchange credential, not an access token. The MCP
        // transport exchanges it for a short-lived session signed for the exact UI transport audience.
        mcp.WithEnvironment("DigitalBrain__Runtime__Ui__BootstrapSecret", uiBootstrapSecret);

        // The Flutter shell references only MCP's authenticated UI transport. It never receives
        // a kernel, Orleans, LLM, legacy Gateway, or WatchHomeFeed reference.
        var flutter = ctx.AddDefaultDevFlutterClient(mcp, uiBootstrapSecret, endpointName: "https")
            ?? throw new InvalidOperationException(
                "Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");

        if (uiOidcIssuer is not null && uiOidcAudience is not null)
        {
            mcp.WithEnvironment("DigitalBrain__Runtime__Ui__Oidc__Issuer", uiOidcIssuer);
            mcp.WithEnvironment("DigitalBrain__Runtime__Ui__Oidc__Audience", uiOidcAudience);
            mcp.WithEnvironment(
                "DigitalBrain__Runtime__Ui__Oidc__AllowedGrants",
                "brain.read,ui.action,gmail.read,gmail.send,salesforce.read,salesforce.write");
            var flutterWeb = ctx.AddDefaultDevFlutterWebClient(
                    mcp,
                    uiOidcIssuer,
                    uiOidcAudience,
                    endpointName: "https",
                    port: flutterWebPort)
                ?? throw new InvalidOperationException(
                    "Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");
        }
    }
}

// LLM env vars (Provider/Model/OllamaEndpoint/AzureOpenAI*) are already wired from typed
// config by ctx.WireKernelSilo(kernel) above — driven by options.WithLLM<TModel>(). Do not
// re-set them here; a second WithEnvironment call for the same key wins last and would
// silently override the typed selection.

builder.Build().Run();

static bool IsLocalUiProfile(string profile) =>
    profile.Equals("Development", StringComparison.OrdinalIgnoreCase)
    || profile.Equals("Test", StringComparison.OrdinalIgnoreCase);

static bool IsKnownRuntimeProfile(string profile) =>
    IsLocalUiProfile(profile)
    || profile.Equals("Production", StringComparison.OrdinalIgnoreCase);

static IResourceBuilder<ParameterResource> AddRuntimeSecret(
    IDistributedApplicationBuilder builder,
    string name,
    bool generateLocalDefault) =>
    generateLocalDefault
        ? builder.AddParameter(name, CreateKeyDefault(), secret: true, persist: true)
        : builder.AddParameter(name, secret: true);

static int? ParseOptionalPort(string? configured)
{
    if (string.IsNullOrWhiteSpace(configured)) return null;
    return int.TryParse(configured, out var port) && port is > 0 and <= 65535
        ? port
        : throw new InvalidOperationException(
            "DigitalBrain:Runtime:Ui:WebPort must be between 1 and 65535.");
}

static GenerateParameterDefault CreateKeyDefault() => new()
{
    // Forty-four base64-alphabet characters decode to 33 bytes. Aspire persists the generated
    // secret in Run mode so signed sessions and encrypted runtime state survive AppHost restarts.
    MinLength = 44,
    Lower = true,
    Upper = true,
    Numeric = true,
    Special = false,
    MinLower = 1,
    MinUpper = 1,
    MinNumeric = 1
};
