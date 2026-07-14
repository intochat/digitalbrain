using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AppHost;
using AnthropicModels = DigitalBrain.Kernel.Contracts.Models.Anthropic;
using GitHubModels = DigitalBrain.Kernel.Contracts.Models.GitHub;
using OllamaModels = DigitalBrain.Kernel.Contracts.Models.Ollama;
using OpenAIModels = DigitalBrain.Kernel.Contracts.Models.OpenAI;

var builder = DistributedApplication.CreateBuilder(args);

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
if (enableDevFlutter && hasAnyUiOidcConfiguration && (string.IsNullOrEmpty(uiOidcIssuer) || string.IsNullOrEmpty(uiOidcAudience)))
    throw new InvalidOperationException("DigitalBrain:Runtime:Ui:Oidc:Issuer and Audience must be configured together for the local browser UI.");
var flutterWebPort = ParseOptionalPort(builder.Configuration["DigitalBrain:Runtime:Ui:WebPort"]);
IResourceBuilder<ParameterResource>? uiBootstrapSecret = enableDevFlutter
    ? builder.AddParameter(
        "runtime-ui-bootstrap-secret",
        () => builder.Configuration["Parameters:runtime-ui-bootstrap-secret"] ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        secret: true)
    : null;

var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{

    options.WithLLM<OllamaModels.Llama31_8B>().AsBalanced().WithEmbedding<OllamaModels.MxbaiEmbedLarge>();

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

var featureHost = builder.AddProject<Projects.DigitalBrain_FeatureHost>("feature-host").WithReference(ctx.FeatureArtifacts).WithEnvironment("DigitalBrain__FeatureHost__InternalOrigin", kernel.GetEndpoint("web"))
    .WithEnvironment("DigitalBrain__FeatureHost__InternalToken", featureHostInternalToken)
    .WithReplicas(1);
ctx.ConfigureClient(featureHost);
featureHost.WaitFor(ctx.FeatureArtifacts);
featureHost.WaitFor(kernel);

builder.AddProject<Projects.DigitalBrain_FeatureBuilder>("feature-builder").WithExplicitStart();

if (ctx.EnableMcp)
{

    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp", launchProfileName: null).WithEnvironment("DigitalBrain__Runtime__OAuth__InternalOrigin", kernel.GetEndpoint("web"))
            .WithEnvironment("DigitalBrain__Auth__SessionSigningKey", sessionSigningKey)
            .WithEnvironment("DigitalBrain__Profile", profile)
            .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", salesforceAppConfig.RedirectUri)
            .WithEnvironment("DigitalBrain__Runtime__Mcp__Audience", DigitalBrain.Kernel.Contracts.Runtime.SessionAudiences.Mcp)
            .WithEnvironment("DigitalBrain__Runtime__Mcp__MaxBodyBytes", "6291456")
            .WithEnvironment("DigitalBrain__Runtime__Transport__MaxBodyBytes", "6291456")
            .WithEnvironment("DigitalBrain__Runtime__Ui__Audience", DigitalBrain.Kernel.Contracts.Runtime.SessionAudiences.Ui)
            .WithEndpoint(name: "http", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithHttpsEndpoint(name: "https", env: "ASPNETCORE_HTTPS_PORTS", isProxied: true)
            .AsHttp2Service()
            .WithEndpoint("https", endpoint => endpoint.Transport = "http2")
            .WithHttpHealthCheck(path: "/health", endpointName: "https")
            .WithReplicas(1);
    ctx.ConfigureClient(mcp);
    mcp.WithReference(ctx.FeatureArtifacts);
    mcp.WaitFor(ctx.ConversationStateBlobs);
    mcp.WaitFor(ctx.SurfaceFeedStateBlobs);
    mcp.WaitFor(ctx.SessionStateBlobs);
    mcp.WaitFor(ctx.FeatureArtifacts);
    mcp.WaitFor(kernel);

    if (uiBootstrapSecret is not null)
    {

        mcp.WithEnvironment("DigitalBrain__Runtime__Ui__BootstrapSecret", uiBootstrapSecret);

        var flutter = ctx.AddDefaultDevFlutterClient(mcp, uiBootstrapSecret, endpointName: "https")
                    ?? throw new InvalidOperationException("Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");

        if (uiOidcIssuer is not null && uiOidcAudience is not null)
        {
            mcp.WithEnvironment("DigitalBrain__Runtime__Ui__Oidc__Issuer", uiOidcIssuer);
            mcp.WithEnvironment("DigitalBrain__Runtime__Ui__Oidc__Audience", uiOidcAudience);
            mcp.WithEnvironment("DigitalBrain__Runtime__Ui__Oidc__AllowedGrants", "brain.read,ui.action,feature.manage,gmail.read,gmail.send,salesforce.read,salesforce.write");
            var flutterWeb = ctx.AddDefaultDevFlutterWebClient(mcp, uiOidcIssuer, uiOidcAudience, endpointName: "https", port: flutterWebPort)
                ?? throw new InvalidOperationException("Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");
        }
    }
}

builder.Build().Run();

static bool IsLocalUiProfile(string profile) =>
    profile.Equals("Development", StringComparison.OrdinalIgnoreCase) || profile.Equals("Test", StringComparison.OrdinalIgnoreCase);

static bool IsKnownRuntimeProfile(string profile) =>
    IsLocalUiProfile(profile) || profile.Equals("Production", StringComparison.OrdinalIgnoreCase);

static IResourceBuilder<ParameterResource> AddRuntimeSecret(IDistributedApplicationBuilder builder, string name, bool generateLocalDefault) =>
    generateLocalDefault
        ? builder.AddParameter(name, CreateKeyDefault(), secret: true, persist: true)
        : builder.AddParameter(name, secret: true);

static int? ParseOptionalPort(string? configured)
{
    if (string.IsNullOrWhiteSpace(configured)) return null;
    return int.TryParse(configured, out var port) && port is > 0 and <= 65535
        ? port
        : throw new InvalidOperationException("DigitalBrain:Runtime:Ui:WebPort must be between 1 and 65535.");
}

static GenerateParameterDefault CreateKeyDefault() => new() { MinLength = 44, Lower = true, Upper = true, Numeric = true, Special = false, MinLower = 1, MinUpper = 1, MinNumeric = 1 };
