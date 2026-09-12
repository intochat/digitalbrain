using Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Aspire.Hosting;
using DigitalBrain.Excel;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Microsoft.Hosting;
using DigitalBrain.Microsoft;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Time;
using DigitalBrain.UI.Aspire.Hosting;
using DigitalBrain.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenAIModels = DigitalBrain.AI.OpenAI;

var builder = DistributedApplication.CreateBuilder(args);
var captureGenAiContent = builder.Configuration.GetValue<bool?>("DigitalBrain:AI:Telemetry:EnableSensitiveData")
    ?? (builder.Environment.IsDevelopment() && builder.ExecutionContext.IsRunMode);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<AIModule>(ai =>
    {
        ai.EnableSensitiveData = captureGenAiContent;

        // --- OpenAI ---
        //ai.WithLlm<OpenAIModels.IGpt56Sol>();
        //ai.WithLlm<OpenAIModels.IGpt56Terra>();
        ai.WithLlm<OpenAIModels.IGpt56Luna>();
        ai.WithDefaultLlm<OpenAIModels.IGpt56Luna>();
        ai.WithEmbedding<OpenAIModels.ITextEmbedding3Small>();
        ai.WithDefaultEmbedding<OpenAIModels.ITextEmbedding3Small>();

        // --- Anthropic ---
        // ai.WithLlm<AnthropicModels.IFable5>();
        // ai.WithLlm<AnthropicModels.ISonnet5>();
        // ai.WithLlm<AnthropicModels.IHaiku45>();
        // ai.WithDefaultLlm<AnthropicModels.IFable5>();

        // --- Google ---
        // ai.WithLlm<GoogleModels.IGemini31Pro>();
        // ai.WithLlm<GoogleModels.IGemini36Flash>();
        // ai.WithDefaultLlm<GoogleModels.IGemini31Pro>();
        // ai.WithEmbedding<GoogleModels.IGeminiEmbedding>();
        // ai.WithDefaultEmbedding<GoogleModels.IGeminiEmbedding>();

        // --- xAI ---
        // ai.WithLlm<XaiModels.IGrok46>();
        // ai.WithDefaultLlm<XaiModels.IGrok46>();

        // --- Ollama ---
        // ai.WithLlm<OllamaModels.IGemma4>();
        // ai.WithLlm<OllamaModels.IQwen35>();
        // ai.WithDefaultLlm<OllamaModels.IQwen35>();
        // ai.WithEmbedding<OllamaModels.IEmbeddingGemma>();
        // ai.WithDefaultEmbedding<OllamaModels.IEmbeddingGemma>();

        ai.WithVoiceToText<IWhisperTiny>();
        ai.WithTavilySearch();
    })
    .AddModule<MemoryModule>(memory => memory.WithQdrant())
    .AddModule<ClickHouseModule>(clickhouse => clickhouse.WithClickHouse(options => options.WithSeed("leads")))
    .AddModule<TimeModule>()
    .AddModule<ExcelModule>()
    .AddModule<GoogleModule>(google => google.WithGmail())
    .AddModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .AddModule<MicrosoftModule>(microsoft => microsoft
        .WithAspire(Path.Combine(builder.AppHostDirectory, "DigitalBrain.AppHost.csproj"))
        .WithConfiguredGitHubRepositories(builder.Configuration))
    .AddModule<UIModule>(ui => ui.WithWindowHost());

// Isolated Aspire runs reuse the persistent Azurite volume while assigning new random silo
// ports. A per-run development cluster avoids trying to contact a dead membership row from the
// previous run; the service id remains stable, so grain and reminder state are still preserved.
var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while 5080 still
    // refuses connections (observed on loaded CI runners).
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithUrlForEndpoint(
        "http",
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        })
    .WithEnvironment(context =>
    {
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }
    });

builder.Build().Run();
