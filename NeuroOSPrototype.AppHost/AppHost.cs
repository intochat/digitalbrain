using DigitalBrain.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Integrations (Telegram, Flutter) packed as marketplace NeuroPacks - no logic inside this AppHost.
// Pack provides the Aspire bits (see AddFlutterClient).

// Experiences emit UiSurface (AuthButtonSurface etc) for sdk/flutter_demo + Telegram skeleton.
var ctx = builder.AddDigitalBrain("digitalbrain", options =>
{
    options.WithLLM<Qwen25Coder1_5B>();
    // options.WithLLM<Gpt4oMini>(); // switch to Azure OpenAI when ready (needs azure-openai-endpoint/-key parameters)
    options.UseLocalMarketplace = true;
})
.WithOrleansDashboard(8080)
.WithMcp();

// Service-to-service secret gating the secrets-returning GetPackConfig RPC. Shared (same value) between the
// kernel and any internal transport that pulls pack config; NEVER injected into the Flutter client config, so a
// browser/untrusted gRPC client on the same external ingress cannot present it. Auto-generated when absent.
var internalServiceKey = builder.AddParameter(
    "internal-service-key",
    () => builder.Configuration["Parameters:internal-service-key"] ?? Guid.NewGuid().ToString("N"),
    secret: true);
var salesforceAppConfig = builder.AddSalesforceAppConfig();

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>("kernel");
ctx.WireKernelSilo(kernel);  // Provides kernel cool features out of box (marketplace, surfaces, journals, 3 replicas HA, LLM for built-ins) via the Aspire package.
kernel.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
kernel.WithSalesforceAppConfig(salesforceAppConfig);

// Default Windows Flutter thin client on local `aspire run` (P0 item 1+12).
// Full UI logic remains in marketplace NeuroPack. Uses shared dev default helper (extracted to Aspire ext; pack can override later).
var flutter = ctx.AddDefaultDevFlutterClient(kernel)
    ?? throw new InvalidOperationException(
        "Flutter app path not resolved for default windows client. Ensure brain/app contains pubspec.yaml or set DIGITALBRAIN_FLUTTER_APP_PATH.");

if (ctx.EnableMcp)
{
    // Expose DigitalBrain MCP (stdio tools) as resource so aspire mcp call can discover registered tools: run_closed_loop, ask_ino, publish_to_marketplace, list_marketplace, etc.
    var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>("mcp")
        .WithReference(ctx.OrleansClient)
        .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm);
}

if (IsEnabled("DIGITALBRAIN_ENABLE_TELEGRAM"))
{
    // Telegram transport: bridges Telegram updates to the kernel gateway over gRPC. It boots no-op without a
    // token, so it is safe to make always-present in production (the spec's "install from marketplace, no
    // restart" intent). Kept behind DIGITALBRAIN_ENABLE_TELEGRAM here so the default product path is unchanged;
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

kernel.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

// LLM env vars (Provider/Model/OllamaEndpoint/AzureOpenAI*) are already wired from typed
// config by ctx.WireKernelSilo(kernel) above — driven by options.WithLLM<TModel>(). Do not
// re-set them here; a second WithEnvironment call for the same key wins last and would
// silently override the typed selection.
if (ctx.EnableOrleansDashboard)
{
    kernel.WithEnvironment("ORLEANS_DASHBOARD_PORT", (ctx.OrleansDashboardPort ?? 8080).ToString());
}

builder.Build().Run();

static bool IsEnabled(string name) =>
    string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.OrdinalIgnoreCase);
