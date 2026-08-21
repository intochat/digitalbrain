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
using Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var personaPlexHuggingFaceToken = builder
    .AddParameter("personaplex-hugging-face-token", secret: true)
    .WithDescription(
        "Required to download PersonaPlex model weights. First [accept the PersonaPlex model access terms](https://huggingface.co/nvidia/personaplex-7b-v1), then [create a read token](https://huggingface.co/settings/tokens). The token is secret and is not sent to the DigitalBrain Kernel.",
        enableMarkdown: true);

// This credential authenticates only the private Kernel-to-adapter stream. Unlike
// HF_TOKEN, it is intentionally shared with the Kernel so it can call /stream.
var personaPlexAdapterToken = builder.AddResource(
    ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
        builder,
        "personaplex-adapter-token"));

#pragma warning disable ASPIREPROBES001 // The readiness probe makes the runtime endpoint contract explicit in the resource model.
var personaPlexRuntime = builder
    .AddDockerfile("personaplex-runtime", "../../Runtime/PersonaPlex")
    // No host proxy/port: only resources on Aspire's private network resolve it.
    .WithEndpoint(
        targetPort: 8080,
        port: null,
        scheme: "http",
        name: "http",
        isExternal: false,
        isProxied: false)
    .WithEnvironment("HF_TOKEN", personaPlexHuggingFaceToken)
    .WithEnvironment("PERSONAPLEX_ADAPTER_TOKEN", personaPlexAdapterToken)
    .WithVolume("personaplex-huggingface-cache", "/var/cache/huggingface")
    .WithContainerRuntimeArgs("--gpus=all")
    .WithHttpProbe(ProbeType.Readiness, "/readyz", endpointName: "http");
#pragma warning restore ASPIREPROBES001

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
        // PersonaPlex now runs behind the private adapter resource. Do not start
        // the legacy in-process ONNX host while the adapter owns CUDA/model state.
        options.Enabled = false;
        options.UseRemoteRuntime = true;
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
    .WithReference(personaPlexRuntime.GetEndpoint("http"))
    .WithEnvironment(
        "DigitalBrain__AI__PersonaPlex__RuntimeEndpoint",
        personaPlexRuntime.GetEndpoint("http"))
    .WithEnvironment(
        "DigitalBrain__AI__PersonaPlex__AdapterToken",
        personaPlexAdapterToken)
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
        })
    .WaitFor(personaPlexRuntime);

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
