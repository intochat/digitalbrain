using Aspire.Hosting;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Execution;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.SmartPrompt;
using DigitalBrain.Time;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;
using AnthropicModels = DigitalBrain.AI.Anthropic;
using GoogleModels = DigitalBrain.AI.Google;
using OllamaModels = DigitalBrain.AI.Ollama;
using OpenAIModels = DigitalBrain.AI.OpenAI;
using XaiModels = DigitalBrain.AI.XAI;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<AIModule>(ai =>
    {
        ai.EnableSensitiveData = false;

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

        //ai.WithVoiceToText<IWhisperLargeV3Turbo>();
    })
    .AddModule<MemoryModule>(memory => memory.WithQdrant())
    .AddModule<TimeModule>()
    .AddModule<ExecutionModule>()
    .AddModule<GoogleModule>(google => google.WithGmail())
    .AddModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp())
    .AddModule<SmartPromptModule>()
    .AddModule<UIModule>(ui =>
    {
        ui.WithWindowHost();
    });

var fakesEnabled = string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Fakes], "1", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration[DigitalBrainNames.Mode], DigitalBrainNames.TestingMode, StringComparison.Ordinal);
if (fakesEnabled)
{
    brain.WithDigitalBrainFakes();
}

// Isolated Aspire runs reuse the persistent Azurite volume while assigning new random silo
// ports. A per-run development cluster avoids trying to contact a dead membership row from the
// previous run; the service id remains stable, so grain and reminder state are still preserved.
var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while 5080 still
    // refuses connections (observed on loaded CI runners).
    .WithHttpHealthCheck("/health", endpointName: ShellHostingExtensions.HttpEndpointName)
    .WithUrlForEndpoint(
        ShellHostingExtensions.HttpEndpointName,
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

var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithMcpServer(ProductSurfaceResources.McpPath, ProductSurfaceResources.McpHttpEndpointName)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment(context =>
    {
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }
    })
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(kernel);

// Later: AddProject<Projects.DigitalBrain_Scripting>(...) as a sibling resource for Script driver IPC.
// Do not reference DigitalBrain.Scripting from Kernel — generated C# stays out of process.

builder.Build().Run();
