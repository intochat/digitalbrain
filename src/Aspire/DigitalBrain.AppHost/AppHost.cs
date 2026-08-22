using DigitalBrain.AI;
using DigitalBrain.AI.Anthropic;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Google;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.AI.XAI;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
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
        ai.WithEmbedding<ITextEmbedding3Small>();
        ai.WithEmbedding<ITextEmbedding3Large>();
        ai.WithEmbedding<IGeminiEmbedding>();
        ai.WithEmbedding<IEmbeddingGemma>();
        ai.WithVoiceToText<IWhisperLargeV3Turbo>();
    })
    .AddModule<MemoryModule>(memory => memory.WithQdrant())
    .AddModule<TimeModule>()
    .AddModule<UIModule>(ui =>
    {
        ui.WithWindowHost();
    });

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
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

builder.Build().Run();
