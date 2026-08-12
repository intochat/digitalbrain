using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// AppHost is the product composition root: brain fabric + modules + runtimes.
var brain = builder
    .AddDigitalBrain(ProductSurfaceResources.Brain)
    .WithLocalDevelopmentOAuthCallback(new Uri(ProductSurfaceResources.LocalDevelopmentOAuthCallbackUri));

brain.AddModule<AIModule>(ai =>
{
    ai.EnableSensitiveData = builder.Environment.IsDevelopment();
    ai.WithLlm<Gemma4>();
    //ai.WithLlm<Llama32>();
    // Local Whisper STT (Foundry Local). Optional: swap IWhisperSmall / IWhisperTiny for weaker GPUs.
    ai.WithVoiceToText<IWhisperLargeV3Turbo>();
});
brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
brain.AddModule<UiModule>(ui => ui.WithWindowHost());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

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

// Client processes share clustering/streams and must wait for a live silo.
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

builder.AddProject<Projects.DigitalBrain_Scripting>(ProductSurfaceResources.Scripting)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WaitFor(kernel);

builder.Build().Run();
