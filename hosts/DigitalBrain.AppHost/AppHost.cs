using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire;
using Microsoft.Extensions.Configuration;
using AnthropicModels = DigitalBrain.Core.Models.Anthropic;
using GitHubModels = DigitalBrain.Core.Models.GitHub;
using OllamaModels = DigitalBrain.Core.Models.Ollama;
using OpenAIModels = DigitalBrain.Core.Models.OpenAI;
using VoiceModels = DigitalBrain.Core.Models.Voice;

var builder = DistributedApplication.CreateBuilder(args);

// V2 is the authoritative local/test composition. Production mutation capabilities remain explicitly gated.
var v2Profile = builder.Configuration["DigitalBrain:Profile"] ?? "Development";
var v2SessionSigningKey = builder.AddParameter(
    "v2-session-signing-key",
    CreateV2KeyDefault(),
    secret: true,
    persist: true);
var v2UiFeedIntegrityKey = builder.AddParameter(
    "v2-ui-feed-integrity-key",
    CreateV2KeyDefault(),
    secret: true,
    persist: true);
var enableV2DevFlutter = builder.ExecutionContext.IsRunMode && IsLocalV2UiProfile(v2Profile);
var localV2DataRoot = IsLocalV2UiProfile(v2Profile)
    ? ResolveLocalV2DataRoot(builder.AppHostDirectory, v2Profile)
    : null;
var v2OperationStorePath = ResolveV2StorePath(builder.Configuration, "DigitalBrain:V2:OperationStorePath", localV2DataRoot, "operations.jsonl");
var v2ProjectionStorePath = ResolveV2StorePath(builder.Configuration, "DigitalBrain:V2:ProjectionStorePath", localV2DataRoot, "projections.jsonl");
var v2SessionStorePath = ResolveV2StorePath(builder.Configuration, "DigitalBrain:V2:SessionStorePath", localV2DataRoot, "sessions.jsonl");
var v2UiFeedStorePath = ResolveV2StorePath(builder.Configuration, "DigitalBrain:V2:Ui:FeedStorePath", localV2DataRoot, "ui-feed.jsonl");
IResourceBuilder<ParameterResource>? v2UiBootstrapSecret = enableV2DevFlutter
    ? builder.AddParameter(
        "v2-ui-bootstrap-secret",
        () => builder.Configuration["Parameters:v2-ui-bootstrap-secret"] ?? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        secret: true)
    : null;

// Integrations stay thin here: INO, Gmail, Salesforce, and optional transports are wired
// through the kernel; Flutter is wired only to MCP's authenticated UI boundary.

// Experiences emit UiSurface (AuthButtonSurface etc) for sdk/flutter_demo + Telegram skeleton.
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

    // --- Local voice-to-text via Whisper with CPU fallback ---
    options.WithVoice2Text<VoiceModels.WhisperLargeV3Turbo>();
});

// Service-to-service secret gating the secrets-returning GetPackConfig RPC. Shared (same value) between the
// kernel and any internal transport that pulls pack config; NEVER injected into the Flutter client config, so a
// browser/untrusted gRPC client on the same external ingress cannot present it. Auto-generated when absent.
var internalServiceKey = builder.AddParameter(
    "internal-service-key",
    () => builder.Configuration["Parameters:internal-service-key"] ?? Guid.NewGuid().ToString("N"),
    secret: true);
var salesforceAppConfig = builder.AddSalesforceAppConfig();
var googleAppConfig = builder.AddGoogleAppConfig();

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);  // Provides surfaces, journals, 3 replicas HA, and LLM wiring via the Aspire package.
kernel.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
kernel.WithEnvironment("DigitalBrain__Profile", v2Profile);
kernel.WithEnvironment("DigitalBrain__Auth__SessionSigningKey", v2SessionSigningKey);
kernel.WithSalesforceAppConfig(salesforceAppConfig);
kernel.WithGoogleAppConfig(googleAppConfig);

if (ctx.EnableMcp)
{
#pragma warning disable ASPIREMCP001
    // Dedicated single-replica HTTP MCP resource; avoids stateful MCP sessions going through the replicated kernel proxy.
    // The local launch profile pins ports that collide with the AppHost's proxy model.
    // Exclude it so Aspire owns randomized proxy and target ports in every isolated session.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp", launchProfileName: null)
        .WithReference(ctx.OrleansClient)
        .WithReference(ctx.Llm)
        .WithEnvironment("DigitalBrain__Auth__SessionSigningKey", v2SessionSigningKey)
        .WithEnvironment("DigitalBrain__Profile", v2Profile)
        .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", salesforceAppConfig.RedirectUri)
        .WithEnvironment("DigitalBrain__V2__Ui__FeedIntegrityKey", v2UiFeedIntegrityKey)
        .WithEnvironment("DigitalBrain__Mcp__EnableAdmin", "false")
        .WithEnvironment("DigitalBrain__Mcp__EnableMutations", v2Profile.Equals("Development", StringComparison.OrdinalIgnoreCase) ? "true" : "false")
        .WithEndpoint(name: "http", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
        .WithHttpsEndpoint(name: "https", env: "ASPNETCORE_HTTPS_PORTS", isProxied: true)
        .AsHttp2Service()
        .WithEndpoint("https", endpoint => endpoint.Transport = "http2")
        .WithHttpHealthCheck(path: "/health", endpointName: "https")
        .WithMcpServer(endpointName: "http");

    SetEnvironmentWhenConfigured(mcp, "DigitalBrain__V2__OperationStorePath", v2OperationStorePath);
    SetEnvironmentWhenConfigured(mcp, "DigitalBrain__V2__ProjectionStorePath", v2ProjectionStorePath);
    SetEnvironmentWhenConfigured(mcp, "DigitalBrain__V2__SessionStorePath", v2SessionStorePath);
    SetEnvironmentWhenConfigured(mcp, "DigitalBrain__V2__Ui__FeedStorePath", v2UiFeedStorePath);

    if (v2UiBootstrapSecret is not null)
    {
        // This is a local, scope-limited exchange credential, not an access token. The MCP
        // transport exchanges it for a short-lived session signed for the exact V2 UI audience.
        mcp.WithEnvironment("DigitalBrain__V2__Ui__BootstrapSecret", v2UiBootstrapSecret);

        // The Flutter shell references only MCP's authenticated UI transport. It never receives
        // a kernel, Orleans, LLM, legacy Gateway, or WatchHomeFeed reference.
        var flutter = ctx.AddDefaultDevV2FlutterClient(mcp, v2UiBootstrapSecret, endpointName: "https")
            ?? throw new InvalidOperationException(
                "Flutter app path not resolved. Ensure app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");
        flutter.WithEnvironment("DIGITALBRAIN_SALESFORCE_OAUTH_CALLBACK", salesforceAppConfig.RedirectUriValue);
    }
#pragma warning restore ASPIREMCP001
}

if (IsEnabled("DIGITALBRAIN_ENABLE_TELEGRAM"))
{
    // Telegram transport: bridges Telegram updates to the kernel gateway over gRPC. It boots no-op without a
    // token, so it is safe to make always-present in production. Kept behind DIGITALBRAIN_ENABLE_TELEGRAM here so the default product path is unchanged;
    // to enable the experience without an AppHost restart, set DIGITALBRAIN_ENABLE_TELEGRAM=true (or drop this gate).
    // The token is an optional secret parameter — supplied at startup (config/user-secrets/env: Parameters:telegram-bot-token)
    // OR later via the in-app config flow. Empty default keeps it optional: no token -> transport boots no-op.
    var telegramBotToken = builder.AddParameter(
        "telegram-bot-token",
        () => builder.Configuration["Parameters:telegram-bot-token"] ?? string.Empty,
        secret: true);
    var telegramTransport = builder.AddProject<Projects.DigitalBrain_Telegram_Transport>("telegram-bot");
    ctx.WireTelegramTransport(telegramTransport, kernel, telegramBotToken, internalServiceKey);
}

// LLM env vars (Provider/Model/OllamaEndpoint/AzureOpenAI*) are already wired from typed
// config by ctx.WireKernelSilo(kernel) above — driven by options.WithLLM<TModel>(). Do not
// re-set them here; a second WithEnvironment call for the same key wins last and would
// silently override the typed selection.

builder.Build().Run();

static bool IsEnabled(string name) =>
    string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.OrdinalIgnoreCase);

static bool IsLocalV2UiProfile(string profile) =>
    profile.Equals("Development", StringComparison.OrdinalIgnoreCase)
    || profile.Equals("Test", StringComparison.OrdinalIgnoreCase);

static GenerateParameterDefault CreateV2KeyDefault() => new()
{
    // Forty-four base64-alphabet characters decode to 33 bytes. Aspire persists the generated
    // secret in Run mode so signed sessions and durable feed integrity survive AppHost restarts.
    MinLength = 44,
    Lower = true,
    Upper = true,
    Numeric = true,
    Special = false,
    MinLower = 1,
    MinUpper = 1,
    MinNumeric = 1
};

static string ResolveLocalV2DataRoot(string appHostDirectory, string profile)
{
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localAppData))
        throw new InvalidOperationException("A private per-user local application-data directory is required for local V2 durability.");
    var fullAppHostPath = Path.GetFullPath(appHostDirectory);
    var canonicalPath = OperatingSystem.IsWindows() ? fullAppHostPath.ToUpperInvariant() : fullAppHostPath;
    var scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(canonicalPath))).ToLowerInvariant()[..16];
    return Path.Combine(localAppData, "DigitalBrain", "V2", scope, profile.ToLowerInvariant());
}

static string? ResolveV2StorePath(
    IConfiguration configuration,
    string configurationKey,
    string? localDataRoot,
    string fileName)
{
    var configured = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
    return localDataRoot is null ? null : Path.Combine(localDataRoot, fileName);
}

static void SetEnvironmentWhenConfigured(
    IResourceBuilder<ProjectResource> resource,
    string name,
    string? value)
{
    if (!string.IsNullOrWhiteSpace(value)) resource.WithEnvironment(name, value);
}
