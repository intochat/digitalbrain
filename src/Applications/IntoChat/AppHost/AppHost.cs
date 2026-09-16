using Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Aspire.Hosting;
using DigitalBrain.Supabase;
using DigitalBrain.Coding.Aspire.Hosting;
using DigitalBrain.Coding;
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
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Flutter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenAIModels = DigitalBrain.AI.OpenAI;

var builder = DistributedApplication.CreateBuilder(args);
var captureGenAiContent = builder.Configuration.GetSection($"{AIOptions.SectionName}:Telemetry")
    .Get<AITelemetryOptions>()?.EnableSensitiveData
    ?? (builder.Environment.IsDevelopment() && builder.ExecutionContext.IsRunMode);

var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules)
    .AddModule<AIModule>(module =>
    {
        module.EnableSensitiveData = captureGenAiContent;

        // --- OpenAI ---
        //module.WithLlm<OpenAIModels.IGpt56Sol>();
        //module.WithLlm<OpenAIModels.IGpt56Terra>();
        module.WithLlm<OpenAIModels.IGpt56Luna>();
        module.WithDefaultLlm<OpenAIModels.IGpt56Luna>();
        module.WithEmbedding<OpenAIModels.ITextEmbedding3Small>();
        module.WithDefaultEmbedding<OpenAIModels.ITextEmbedding3Small>();

        // --- Anthropic ---
        // module.WithLlm<AnthropicModels.IFable5>();
        // module.WithLlm<AnthropicModels.ISonnet5>();
        // module.WithLlm<AnthropicModels.IHaiku45>();
        // module.WithDefaultLlm<AnthropicModels.IFable5>();

        // --- Google ---
        // module.WithLlm<GoogleModels.IGemini31Pro>();
        // module.WithLlm<GoogleModels.IGemini36Flash>();
        // module.WithDefaultLlm<GoogleModels.IGemini31Pro>();
        // module.WithEmbedding<GoogleModels.IGeminiEmbedding>();
        // module.WithDefaultEmbedding<GoogleModels.IGeminiEmbedding>();

        // --- xAI ---
        // module.WithLlm<XaiModels.IGrok46>();
        // module.WithDefaultLlm<XaiModels.IGrok46>();

        // --- Ollama ---
        // module.WithLlm<OllamaModels.IGemma4>();
        // module.WithLlm<OllamaModels.IQwen35>();
        // module.WithDefaultLlm<OllamaModels.IQwen35>();
        // module.WithEmbedding<OllamaModels.IEmbeddingGemma>();
        // module.WithDefaultEmbedding<OllamaModels.IEmbeddingGemma>();

        module.WithVoiceToText<IWhisperLargeV3Turbo>();
        module.WithTavilySearch();
    })
    .AddModule<MemoryModule>(module => module.WithQdrant())
    .AddModule<ClickHouseModule>(module =>
        module.WithClickHouse(options =>
            options.WithSeed("leads")))
    .AddModule<SupabaseModule>()
    .AddModule<TimeModule>()
    .AddModule<ExcelModule>()
    .AddModule<GoogleModule>(module => module.WithGmail())
    .AddModule<SalesforceModule>(module => module.WithHostedMcp())
    .AddModule<MicrosoftModule>(module => module
        .WithAspire(Path.Combine(builder.AppHostDirectory, "IntoChat.AppHost.csproj"))
        .WithConfiguredGitHubRepositories(builder.Configuration))
    .AddModule<CodingModule>(module => module.WithSolution(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", "DigitalBrain.slnx")))
    .AddModule<FlutterModule>(module => module.WithWindowHost());

// Isolated Aspire runs reuse the persistent Azurite volume while assigning new random silo
// ports. A per-run development cluster avoids trying to contact a dead membership row from the
// previous run; the service id remains stable, so grain and reminder state are still preserved.
var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
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

        // The typed neuron surface (/mcp describe and call) is how Claude Code and Codex reach the coding tools.
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
    });

builder.Build().Run();
