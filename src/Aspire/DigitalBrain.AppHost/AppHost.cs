using Aspire.Hosting;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.FoundryLocal;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Aspire.Hosting;
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
    .AddModule<CodingModule>(coding => coding.WithSolution(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "DigitalBrain.slnx")))
    .AddModule<UIModule>(ui => ui.WithWindowHost());

// Both slots run the same silo against one ServiceId, so grain state, journals and reminders survive a
// swap. Neither gets an Orleans__ClusterId: the silo mints "{slot}-{timestamp}" per start, because a
// reused cluster id makes the new silo stall on the previous one's dead membership row (R5.3, spike S2).
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
var standbyDll = Path.Combine(repositoryRoot, "artifacts", "slot-b", "bin", "DigitalBrain.Silo", "release", "DigitalBrain.Silo.dll");

// Declared before the kernels on purpose: the UI module's projection binds DIGITALBRAIN_UI_BASE to the
// first resource that takes the brain reference, and the shell must talk to the gateway, never to a slot.
var gateway = builder.AddProject<Projects.DigitalBrain_Gateway>(ProductSurfaceResources.Gateway)
    .WithReference(brain)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.GatewayHttpPort,
        name: "http",
        isProxied: false)
    .WithEnvironment("DigitalBrain__Gateway__Slots__a", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Gateway__Slots__b", ProductSurfaceResources.KernelBUrl)
    .WithEnvironment("DigitalBrain__Gateway__Active", "a")
    // Its own liveness, not the product's: /health is proxied to whichever slot is active.
    .WithHttpHealthCheck("/active", endpointName: "http");

var kernelA = builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.KernelA)
    .WithReference(brain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("DigitalBrain__Slot", "a")
    .WithEnvironment("DigitalBrain__Slots__Gateway", ProductSurfaceResources.GatewayUrl)
    // The same root the standby dll above is taken from: the builder and the AppHost must not drift.
    .WithEnvironment("DigitalBrain__Slots__ArtifactsRoot", Path.Combine(repositoryRoot, "artifacts"))
    .WithEnvironment("DigitalBrain__Slots__a__Url", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Slots__b__Url", ProductSurfaceResources.KernelBUrl)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.KernelAHttpPort,
        name: "http",
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while the port still
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
        // The typed neuron surface (/mcp describe and call) is how Claude Code and Codex reach the coding tools.
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
    });

// The standby is an executable over its own build output: two AddProject resources from one project share
// one output, so they cannot differ by ArtifactsPath (spike S1). Build it with
// dotnet build DigitalBrain.slnx -c Release -p:ArtifactsPath=<repo>/artifacts/slot-b -p:UseArtifactsOutput=true
// before starting it; the slot neuron does exactly that.
var kernelB = builder.AddExecutable(ProductSurfaceResources.KernelB, "dotnet", repositoryRoot, standbyDll)
    .WithReference(brain)
    .WithExplicitStart()
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("DigitalBrain__Slot", "b")
    .WithEnvironment("DigitalBrain__Slots__Gateway", ProductSurfaceResources.GatewayUrl)
    .WithEnvironment("DigitalBrain__Slots__ArtifactsRoot", Path.Combine(repositoryRoot, "artifacts"))
    .WithEnvironment("DigitalBrain__Slots__a__Url", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Slots__b__Url", ProductSurfaceResources.KernelBUrl)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.KernelBHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment(context =>
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
    });

// An executable gets no ASPNETCORE_URLS from WithHttpEndpoint, and a proxied endpoint's address is the
// proxy's own, which the process then collides with: both halves of spike S1's fix.
kernelB.WithEnvironment("ASPNETCORE_URLS", kernelB.GetEndpoint("http"));

// The gateway proxies to kernel-a first, so it waits for it rather than answering 502 to the shell.
gateway.WaitFor(kernelA);

builder.Build().Run();
