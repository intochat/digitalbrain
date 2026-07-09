using Aspire.Hosting;
using DigitalBrain.Aspire;
using AnthropicModels = DigitalBrain.Core.Models.Anthropic;
using GitHubModels = DigitalBrain.Core.Models.GitHub;
using OllamaModels = DigitalBrain.Core.Models.Ollama;
using OpenAIModels = DigitalBrain.Core.Models.OpenAI;
using VoiceModels = DigitalBrain.Core.Models.Voice;

var builder = DistributedApplication.CreateBuilder(args);

// Integrations stay thin here: INO, Gmail, Salesforce, Flutter, and optional transports
// are wired through the kernel rather than distributed through a marketplace.

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
kernel.WithSalesforceAppConfig(salesforceAppConfig);
kernel.WithGoogleAppConfig(googleAppConfig);

// Default Windows Flutter thin client on local `aspire run` (P0 item 1+12).
// Flutter is the thin local client for neuron-emitted surfaces.
var flutter = ctx.AddDefaultDevFlutterClient(kernel)
    ?? throw new InvalidOperationException(
        "Flutter app path not resolved for default windows client. Ensure brain/app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");

if (ctx.EnableMcp)
{
#pragma warning disable ASPIREMCP001
    // Dedicated single-replica HTTP MCP resource; avoids stateful MCP sessions going through the replicated kernel proxy.
    builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference(ctx.Llm)
        .WithEnvironment("DIGITALBRAIN_MCP_TRANSPORT", "http")
        .WithEndpoint(name: "http", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
        .WithMcpServer(endpointName: "http");
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
