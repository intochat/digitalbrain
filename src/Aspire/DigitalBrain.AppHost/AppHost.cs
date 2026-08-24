using DigitalBrain.AI;
using DigitalBrain.AI.Anthropic;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Google;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.AI.XAI;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Execution;
using DigitalBrain.Integrations;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.SmartPrompt;
using DigitalBrain.Time;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<AIModule>(ai =>
    {
        ai.EnableSensitiveData = builder.Environment.IsDevelopment();
        ai.WithLlm<IGpt54>();
        ai.WithLlm<IGpt54Mini>();
        ai.WithLlm<IGpt54Nano>();
        ai.WithLlm<IOpus5>();
        ai.WithLlm<ISonnet5>();
        ai.WithLlm<IHaiku45>();
        ai.WithLlm<IGemini36Pro>();
        ai.WithLlm<IGemini36Flash>();
        ai.WithLlm<IGrok46>();
        ai.WithLlm<IGemma4>();
        ai.WithDefaultLlm<IGemma4>();
        ai.WithEmbedding<ITextEmbedding3Small>();
        ai.WithEmbedding<ITextEmbedding3Large>();
        ai.WithEmbedding<IGeminiEmbedding>();
        ai.WithEmbedding<IEmbeddingGemma>();
        ai.WithDefaultEmbedding<IEmbeddingGemma>();
        ai.WithVoiceToText<IWhisperLargeV3Turbo>();
    })
    .AddModule<MemoryModule>(memory => memory.WithQdrant())
    .AddModule<TimeModule>()
    .AddModule<ExecutionModule>()
    .AddModule<IntegrationsModule>()
    .AddModule<SmartPromptModule>()
    .AddModule<UIModule>(ui =>
    {
        ui.WithWindowHost();
    });

if (builder.Environment.IsDevelopment())
{
    brain.WithDigitalBrainFakes();
}

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
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
        });

var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithMcpServer(ProductSurfaceResources.McpPath, ProductSurfaceResources.McpHttpEndpointName)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(kernel);

// Later: AddProject<Projects.DigitalBrain_Scripting>(...) as a sibling resource for Script driver IPC.
// Do not reference DigitalBrain.Scripting from Kernel — generated C# stays out of process.

builder.Build().Run();
