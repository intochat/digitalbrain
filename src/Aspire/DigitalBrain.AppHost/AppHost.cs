using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Time;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// AppHost is the product composition root: brain fabric + modules + runtimes.
var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain);

brain.AddModule<AIModule>(ai =>
{
    ai.EnableSensitiveData = builder.Environment.IsDevelopment();
    ai.WithLlm<IGemma4>();
    ai.WithEmbedding<IEmbeddingGemma>();
    // Local Whisper STT (Foundry Local). Optional: swap IWhisperSmall / IWhisperTiny for weaker GPUs.
    ai.WithVoiceToText<IWhisperLargeV3Turbo>();
    ai.WithPersonaPlex(options =>
    {
        options.Enabled = bool.TryParse(
            builder.Configuration["AppHost:PersonaPlex:Enabled"],
            out var enabled)
            && enabled;
        options.ModelDirectory = builder.Configuration["AppHost:PersonaPlex:ModelDirectory"] ?? string.Empty;
        options.CudaDeviceId = int.TryParse(
            builder.Configuration["AppHost:PersonaPlex:CudaDeviceId"],
            out var cudaDeviceId)
            ? cudaDeviceId
            : 0;
        options.MaxSessions = int.TryParse(
            builder.Configuration["AppHost:PersonaPlex:MaxSessions"],
            out var maxSessions)
            ? maxSessions
            : 1;
    });
});
brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
brain.AddModule<TimeModule>();
// AppHost:UiHost=web selects the headless-web shell (e2e evidence); default stays the desktop window.
var uiHostValue = builder.Configuration["AppHost:UiHost"];
brain.AddModule<UiModule>(ui =>
{
    if (string.Equals(uiHostValue, "web", StringComparison.OrdinalIgnoreCase))
    {
        ui.WithWebHost();
    }
    else
    {
        ui.WithWindowHost();
    }
});
// Silo: waits for Azurite/Orleans fabric (+ module projections such as Ollama/Qdrant)
// via WithReference(brain) → WaitUntilHealthy on brain startup dependencies.
var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint(
        ShellHostingExtensions.HttpEndpointName,
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        });

// Client processes share clustering and must wait for a live silo.
var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(kernel);

#pragma warning disable ASPIREMCP001
mcp.WithMcpServer(
    ProductSurfaceResources.McpPath,
    ProductSurfaceResources.McpHttpEndpointName);
#pragma warning restore ASPIREMCP001

builder.Build().Run();
