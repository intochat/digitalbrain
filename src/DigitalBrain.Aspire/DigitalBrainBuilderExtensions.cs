using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Core.Models;

namespace DigitalBrain.Aspire;

public sealed class DigitalBrainContext
{
    public required string Name { get; init; }
    public required IDistributedApplicationBuilder ApplicationBuilder { get; init; }
    public required OrleansService Orleans { get; init; }
    public required IResourceBuilder<IResourceWithConnectionString> Llm { get; init; }
    public IResourceBuilder<IResourceWithConnectionString>? EmbeddingModel { get; init; }
    public required OrleansServiceClient OrleansClient { get; init; }
    public required int KernelReplicas { get; init; }
    public required DigitalBrainModelRegistry ModelRegistry { get; init; }

    // The resolved LLM model name (e.g. "qwen2.5-coder:1.5b") for env injection.
    public required string LlmModel { get; init; }

    // The resolved LLM provider ("ollama" | "azureopenai"), set via DigitalBrainOptions.WithLLM<TModel>().
    public required string LlmProvider { get; init; }

    // Ollama container http endpoint for DigitalBrain__Llm__OllamaEndpoint injection; null in publish mode
    // (no local Ollama container — the else branch in AddDigitalBrain uses a connection-string placeholder).
    public EndpointReference? OllamaEndpoint { get; init; }

    // Same Ollama container's http endpoint, for DigitalBrain__Embedding__OllamaEndpoint injection; null in
    // publish mode for the same reason as OllamaEndpoint (no local Ollama container to point at).
    public EndpointReference? EmbeddingOllamaEndpoint { get; init; }

    // Local Whisper (speaches) container http endpoint for DigitalBrain__Voice__Endpoint injection; null in
    // publish mode (no local Whisper container — externally-hosted voice endpoints are wired via a manually
    // configured DigitalBrain:Voice:Endpoint instead, same as prod LLM/embedding wiring).
    public EndpointReference? WhisperEndpoint { get; init; }

    // Set only when LlmProvider is "azureopenai" (WithLLM<TModel>() where TModel.Provider == "azureopenai")
    public IResourceBuilder<ParameterResource>? AzureOpenAIEndpoint { get; init; }
    public IResourceBuilder<ParameterResource>? AzureOpenAIKey { get; init; }

    // Set only when the model registry has at least one "anthropic" registration (any role, not just the
    // single-model LlmProvider selection — see AddDigitalBrain's anthropicApiKey block).
    public IResourceBuilder<ParameterResource>? AnthropicApiKey { get; init; }

    // Set only when the model registry has at least one "xai" registration, same lazy pattern as AnthropicApiKey.
    public IResourceBuilder<ParameterResource>? XaiApiKey { get; init; }

    // Storage resources exposed so AppHost can wire WithReference on kernel
    public required IResourceBuilder<AzureBlobStorageResource> GrainBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> JournalBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> SyncBlobs { get; init; }
    public required IResourceBuilder<AzureTableStorageResource> ClusteringTable { get; init; }

    // For encapsulated dashboard + MCP (set from DigitalBrainOptions at construction, single source of truth)
    public bool EnableOrleansDashboard { get; init; }
    public int? OrleansDashboardPort { get; init; }
    public bool EnableMcp { get; init; }
}

public static class DigitalBrainBuilderExtensions
{
    public const int DefaultKernelWebPort = 51014;

    public static DigitalBrainContext AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "digitalbrain",
        Action<DigitalBrainOptions>? configure = null)
    {
        var options = new DigitalBrainOptions();
        configure?.Invoke(options);
        if (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS"), out var replicaOverride) && replicaOverride > 0)
        {
            options.KernelReplicas = replicaOverride;
        }

        var llmProvider = options.ResolvedLlmProvider;
        var llmModel = options.ResolvedLlmModel ?? (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase)
            ? "gpt-4o-mini"
            : "qwen2.5-coder:1.5b");

        IResourceBuilder<ParameterResource>? azureOpenAIEndpoint = null;
        IResourceBuilder<ParameterResource>? azureOpenAIKey = null;
        if (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
        {
            azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint");
            azureOpenAIKey = builder.AddParameter("azure-openai-key", secret: true);
        }

        IResourceBuilder<ParameterResource>? anthropicApiKey = null;
        if (options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase)))
        {
            anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
        }

        IResourceBuilder<ParameterResource>? xaiApiKey = null;
        if (options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.Xai, StringComparison.OrdinalIgnoreCase)))
        {
            xaiApiKey = builder.AddParameter("xai-api-key", secret: true);
        }

        var isRunMode = builder.ExecutionContext.IsRunMode;

        // No publish-mode else branch needed here (unlike Ollama below): AddAzureStorage already
        // produces a valid real-Azure resource on its own — RunAsEmulator() is purely a run-mode add-on.
        var storage = builder.AddAzureStorage("storage");
        if (isRunMode)
        {
            storage.RunAsEmulator(azurite =>
            {
                azurite.WithLifetime(ContainerLifetime.Persistent);
            });
        }
        var clusteringTable = storage.AddTables("clustering");
        var grainBlobs = storage.AddBlobs("grainstate");
        var journalBlobs = storage.AddBlobs("journal");
        var syncBlobs = storage.AddBlobs("sync");

        var orleans = builder.AddOrleans("kernel")
            .WithClustering(clusteringTable)
            .WithGrainStorage("Default", grainBlobs)
            .WithReminders(clusteringTable);

        if (isRunMode)
        {
            // Persistent Azurite keeps Orleans membership rows; use a fresh local cluster id so
            // stale active silos from killed/restarted replicas do not block the next startup.
            orleans.WithClusterId(ResolveLocalClusterId());
        }

        // Ollama always runs as the offline fallback (per DEMO-PLAN), independent of the chosen primary
        // provider — it must pull its own real model tag, never the primary provider's model/deployment
        // name (e.g. an azureopenai deployment name like "gpt-4o-mini" is not a pullable Ollama tag). But
        // only in run mode: `aspire publish` should never emit a local Ollama container into a publish
        // manifest — prod gets its LLM from Azure OpenAI via Pulumi, wired separately (see WireKernelSilo method).
        const string ollamaFallbackModel = "qwen2.5-coder:1.5b";
        IResourceBuilder<IResourceWithConnectionString> qwen;
        IResourceBuilder<IResourceWithConnectionString>? embeddingModel = null;
        EndpointReference? ollamaEndpoint = null;
        EndpointReference? embeddingOllamaEndpoint = null;
        if (isRunMode)
        {
            var ollama = builder.AddOllama("ollama")
                .WithGPUSupport()
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .WithOpenWebUI(webui => webui.WithLifetime(ContainerLifetime.Persistent).WithDataVolume());
            qwen = ollama.AddModel("qwen", ollamaFallbackModel);
            embeddingModel = ollama.AddModel("embed", "nomic-embed-text");

            // Pre-pull every other distinct Ollama LLM tag in the registry (e.g. Llama31_8B, registered
            // .AsReasoning() for Ino's tool-calling path) into this same container. Without this, a model
            // the registry points Ino at would never actually exist in the running container, and the first
            // real chat call to it would fail with a "model not found" error from Ollama.
            var pulledOllamaModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ollamaFallbackModel };
            foreach (var entry in options.ModelRegistry.Registrations)
            {
                if (entry.Model.Kind != DigitalBrainCapabilityKind.LargeLanguageModel ||
                    !string.Equals(entry.Model.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase) ||
                    !pulledOllamaModelIds.Add(entry.Model.Id))
                {
                    continue;
                }
                ollama.AddModel(OllamaModelResourceName(entry.Model.Id), entry.Model.Id);
            }

            ollamaEndpoint = ollama.GetEndpoint("http");
            // Same container as qwen — Ollama serves every pulled model from one endpoint,
            // selected by model name in the request, not by a per-model endpoint.
            embeddingOllamaEndpoint = ollamaEndpoint;
        }
        else
        {
            qwen = builder.AddConnectionString("qwen");
        }

        // Local Whisper server for voice-to-text (speech-to-text), same run-mode-only guard as Ollama above:
        // `aspire publish` never emits this container into a publish manifest — prod voice endpoints are wired
        // externally via a manually configured DigitalBrain:Voice:Endpoint (see WireKernelSilo).
        //
        // Image: ghcr.io/speaches-ai/speaches (the actively-maintained continuation of fedirz/faster-whisper-server,
        // which was archived and renamed upstream — see https://github.com/speaches-ai/speaches). It genuinely
        // speaks the OpenAI-compatible POST /v1/audio/transcriptions contract that VoiceTranscription.cs's
        // OpenAICompatibleVoiceTranscriber calls, unlike onerahmet/openai-whisper-asr-webservice's non-OpenAI-shaped
        // /asr route. The "-cpu" tag avoids requiring host GPU/CUDA drivers for local dev; swap to "latest-cuda" and
        // add container GPU args if faster inference is needed. "whisper-1" (Whisper1Local.Id) resolves via
        // speaches' built-in model alias map to Systran/faster-whisper-large-v3 with no extra config required.
        EndpointReference? whisperEndpoint = null;
        if (isRunMode)
        {
            var whisper = builder.AddContainer("whisper", "speaches-ai/speaches")
                .WithImageRegistry("ghcr.io")
                .WithImageTag("latest-cpu")
                .WithEnvironment("ENABLE_UI", "false")
                .WithEnvironment("WHISPER__COMPUTE_TYPE", "int8")
                .WithHttpEndpoint(targetPort: 8000, name: "http")
                .WithVolume("whisper-cache", "/home/ubuntu/.cache/huggingface/hub");
            whisperEndpoint = whisper.GetEndpoint("http");
        }

        return new DigitalBrainContext
        {
            Name = name,
            ApplicationBuilder = builder,
            Orleans = orleans,
            Llm = qwen,
            EmbeddingModel = embeddingModel,
            OrleansClient = orleans.AsClient(),
            KernelReplicas = options.KernelReplicas,
            ModelRegistry = options.ModelRegistry.Snapshot(),
            LlmModel = llmModel,
            LlmProvider = llmProvider,
            OllamaEndpoint = ollamaEndpoint,
            EmbeddingOllamaEndpoint = embeddingOllamaEndpoint,
            WhisperEndpoint = whisperEndpoint,
            AzureOpenAIEndpoint = azureOpenAIEndpoint,
            AzureOpenAIKey = azureOpenAIKey,
            AnthropicApiKey = anthropicApiKey,
            XaiApiKey = xaiApiKey,
            EnableOrleansDashboard = options.EnableOrleansDashboard,
            OrleansDashboardPort = options.OrleansDashboardPort,
            EnableMcp = options.EnableMcp,
            GrainBlobs = grainBlobs,
            JournalBlobs = journalBlobs,
            SyncBlobs = syncBlobs,
            ClusteringTable = clusteringTable
        };
    }

    /// <summary>
    /// Wires a kernel project with the core kernel features out of the box:
    /// dynamic UI surfaces, journals, clustering, LLM, and replica count for HA.
    /// This makes the kernel provide built-in capabilities (INO, automations, status, tasks, etc.)
    /// immediately when the kernel starts.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WireKernelSilo(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        kernel = kernel
            .WithReference(ctx.Orleans)
            .WithReference(ctx.ClusteringTable)
            .WithReference(ctx.GrainBlobs)
            .WithReference(ctx.JournalBlobs)
            .WithReference(ctx.SyncBlobs)
            .WithReference(ctx.Llm)
            .WithEndpoint(name: "grpc", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithEndpoint(
                name: "web",
                scheme: "http",
                port: KernelWebPort(ctx.ApplicationBuilder),
                env: "DIGITALBRAIN_WEB_PORT",
                isProxied: true)
            .WithExternalHttpEndpoints()
            .WithReplicas(ctx.KernelReplicas);

        // Ensure storage emulator (Azurite) resources are healthy before launching kernel process.
        // This sequences azurite ready (for clustering/journals/grainstate/sync) ahead of silo init + health probe, reducing races and time-to-healthy in E2E/CI.
        // LLM waits already present below; storage waits complement the WithReference calls.
        kernel.WaitFor(ctx.ClusteringTable);
        kernel.WaitFor(ctx.GrainBlobs);
        kernel.WaitFor(ctx.JournalBlobs);
        kernel.WaitFor(ctx.SyncBlobs);

        kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

        // LLM for kernel built-ins (INO, status diagnosis, code gen, tasks). Provider/model come from
        // DigitalBrainOptions.WithLLM<TModel>() (see DigitalBrain.Core/Models/DigitalBrainModels.cs) rather than a hardcoded string.
        kernel.WithEnvironment("DigitalBrain__Llm__Provider", ctx.LlmProvider);
        kernel.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
        if (ctx.OllamaEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint", HttpUrl(ctx.OllamaEndpoint));
            kernel.WaitFor(ctx.Llm);
        }
        if (ctx.EmbeddingOllamaEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Embedding__OllamaEndpoint", HttpUrl(ctx.EmbeddingOllamaEndpoint));
            if (ctx.EmbeddingModel is not null)
            {
                kernel.WaitFor(ctx.EmbeddingModel);
            }
        }
        kernel.WithModelRegistry(ctx);

        if (ctx.AzureOpenAIEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AzureOpenAIEndpoint", ctx.AzureOpenAIEndpoint);
        }
        if (ctx.AzureOpenAIKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AzureOpenAIKey", ctx.AzureOpenAIKey);
        }
        if (ctx.AnthropicApiKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AnthropicApiKey", ctx.AnthropicApiKey);
        }
        if (ctx.XaiApiKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__XaiApiKey", ctx.XaiApiKey);
        }

        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Provider", "DIGITALBRAIN_VOICE_PROVIDER", "DigitalBrain__Voice__Provider");
        kernel.WithOptionalEnvironment("DigitalBrain:Voice:Model", "DIGITALBRAIN_VOICE_MODEL", "DigitalBrain__Voice__Model");

        // A manually configured endpoint (e.g. an externally-hosted Whisper/OpenAI service) always wins, for
        // backward compat with anyone already relying on it. Otherwise, default to the local Whisper container
        // (see ctx.WhisperEndpoint above) whenever one is present (run mode only) — /v1 is the path segment
        // speaches (ghcr.io/speaches-ai/speaches) exposes its OpenAI-compatible route under; TranscriptionEndpoint
        // in VoiceTranscription.cs appends "/audio/transcriptions" on top of this base.
        var manualVoiceEndpoint = ctx.ApplicationBuilder.Configuration["DigitalBrain:Voice:Endpoint"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_VOICE_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(manualVoiceEndpoint))
        {
            kernel.WithEnvironment("DigitalBrain__Voice__Endpoint", manualVoiceEndpoint);
        }
        else if (ctx.WhisperEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Voice__Endpoint", HttpUrl(ctx.WhisperEndpoint, "/v1"));
        }

        kernel.WithOptionalEnvironment("DigitalBrain:Voice:ApiKey", "DIGITALBRAIN_VOICE_API_KEY", "DigitalBrain__Voice__ApiKey");

        if (ctx.EnableOrleansDashboard && ctx.OrleansDashboardPort.HasValue)
        {
            kernel.WithEnvironment("ORLEANS_DASHBOARD_PORT", ctx.OrleansDashboardPort.Value.ToString());
        }

        return kernel;
    }

    // Builds "http://{host}:{port}{pathSuffix}" against a container endpoint discovered at orchestration time
    // (Ollama, its embedding alias, or Whisper) — shared by the three call sites in WireKernelSilo method above so the
    // host/port interpolation lives in exactly one place.
    private static ReferenceExpression HttpUrl(EndpointReference endpoint, string pathSuffix = "") =>
        ReferenceExpression.Create($"http://{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}{pathSuffix}");

    // Aspire resource names must be lowercase alphanumeric plus hyphens; Ollama tags routinely carry ':' and
    // '.' (e.g. "llama3.1:8b"), so normalize the same way DigitalBrainModelDescriptor.ServiceKey does.
    private static string OllamaModelResourceName(string modelId) =>
        modelId.Replace(':', '-').Replace('.', '-').ToLowerInvariant();

    private static void WithModelRegistry(this IResourceBuilder<ProjectResource> kernel, DigitalBrainContext ctx)
    {
        if (ctx.ModelRegistry.DefaultLlm is not null)
        {
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultLlm__Kind", DigitalBrainCapabilityKind.LargeLanguageModel.ToString());
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultLlm__Provider", ctx.LlmProvider);
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultLlm__Id", ctx.LlmModel);
        }

        if (ctx.ModelRegistry.DefaultEmbedding is { } defaultEmbedding)
        {
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Kind", DigitalBrainCapabilityKind.Embedding.ToString());
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Provider", defaultEmbedding.Model.Provider);
            kernel.WithEnvironment("DigitalBrain__ModelRegistry__DefaultEmbedding__Id", defaultEmbedding.Model.Id);
        }

        for (var i = 0; i < ctx.ModelRegistry.Registrations.Count; i++)
        {
            var registration = ctx.ModelRegistry.Registrations[i];
            var prefix = $"DigitalBrain__ModelRegistry__Registrations__{i}";
            kernel.WithEnvironment($"{prefix}__Kind", registration.Model.Kind.ToString());
            kernel.WithEnvironment($"{prefix}__Provider", registration.Model.Provider);
            kernel.WithEnvironment($"{prefix}__Id", registration.Model.Id);
            kernel.WithEnvironment($"{prefix}__DisplayName", registration.Model.DisplayName);
            kernel.WithEnvironment($"{prefix}__Role", registration.Role.ToString());
            kernel.WithEnvironment($"{prefix}__ServiceKey", registration.Model.ServiceKey);
            kernel.WithEnvironment($"{prefix}__SupportsTools", registration.Model.Capabilities.SupportsTools.ToString());
            kernel.WithEnvironment($"{prefix}__SupportsVision", registration.Model.Capabilities.SupportsVision.ToString());
            kernel.WithEnvironment($"{prefix}__SupportsStreaming", registration.Model.Capabilities.SupportsStreaming.ToString());
            kernel.WithEnvironment($"{prefix}__SupportsStructuredOutput", registration.Model.Capabilities.SupportsStructuredOutput.ToString());
        }
    }

    private static void WithOptionalEnvironment(
        this IResourceBuilder<ProjectResource> resource,
        string configurationKey,
        string environmentKey,
        string targetKey)
    {
        var value = resource.ApplicationBuilder.Configuration[configurationKey]
            ?? Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            resource.WithEnvironment(targetKey, value);
        }
    }

    public static int KernelWebPort(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Kernel:WebPort"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_WEB_PORT");

        return int.TryParse(configured, out var port) && port > 0
            ? port
            : DefaultKernelWebPort;
    }

    internal static string ResolveLocalClusterId() => ResolveLocalClusterId(Environment.GetEnvironmentVariable);

    internal static string ResolveLocalClusterId(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        return getEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID")
            ?? getEnvironmentVariable("DigitalBrain__ClusterId")
            ?? getEnvironmentVariable("Orleans__ClusterId")
            ?? $"digitalbrain-dev-{Guid.NewGuid():N}";
    }

}

public sealed class DigitalBrainOptions
{
    private int? lastModelRegistration;
    private string? llmModel;
    private string llmProvider = DigitalBrainProviderIds.Ollama;
    private bool llmModelOverridden;
    private bool llmProviderOverridden;

    /// <summary>
    /// Provider/model capabilities declared by the AppHost.
    /// </summary>
    public DigitalBrainModelRegistry ModelRegistry { get; } = new();

    /// <summary>
    /// Optional single-model runtime override. Leave unset when using the model registry roles.
    /// </summary>
    public string? LlmModel
    {
        get => llmModel;
        set
        {
            llmModel = value;
            llmModelOverridden = true;
        }
    }

    /// <summary>
    /// Optional single-provider runtime override. Leave unset when using the model registry roles.
    /// </summary>
    public string LlmProvider
    {
        get => llmProvider;
        set
        {
            llmProvider = value;
            llmProviderOverridden = true;
        }
    }

    /// <summary>
    /// Provider that will be injected into the current single-model kernel runtime.
    /// </summary>
    public string ResolvedLlmProvider =>
        !llmProviderOverridden && ModelRegistry.DefaultLlm is { } defaultLlm
            ? defaultLlm.Model.Provider
            : llmProvider;

    /// <summary>
    /// Model id or deployment name that will be injected into the current single-model kernel runtime.
    /// </summary>
    public string? ResolvedLlmModel =>
        !llmModelOverridden && ModelRegistry.DefaultLlm is { } defaultLlm
            ? defaultLlm.Model.Id
            : llmModel;

    public int KernelReplicas { get; set; } = 3;

    public bool EnableOrleansDashboard { get; set; } = true;
    public int? OrleansDashboardPort { get; set; } = 8080;
    public bool EnableMcp { get; set; } = true;

    /// <summary>
    /// Registers an LLM capability and makes it the kernel's current single-model runtime selection.
    /// </summary>
    public DigitalBrainOptions WithLLM<TModel>() where TModel : LlmModel, new()
    {
        var model = new TModel();
        lastModelRegistration = ModelRegistry.Register(model.Describe(), DigitalBrainModelRole.Balanced);
        SelectLlm(model);
        return this;
    }

    /// <summary>
    /// Registers an embedding model for the future context/vector pipeline without changing chat routing.
    /// </summary>
    public DigitalBrainOptions WithEmbedding<TModel>() where TModel : EmbeddingModel, new()
    {
        var model = new TModel();
        lastModelRegistration = ModelRegistry.Register(model.Describe(), DigitalBrainModelRole.Default);
        return this;
    }

    /// <summary>
    /// Registers a voice-to-text model without changing chat routing.
    /// </summary>
    public DigitalBrainOptions WithVoice2Text<TModel>() where TModel : VoiceToTextModel, new()
    {
        var model = new TModel();
        lastModelRegistration = ModelRegistry.Register(model.Describe(), DigitalBrainModelRole.Default);
        return this;
    }

    /// <summary>
    /// Registers the vector database provider intended for embedding persistence.
    /// </summary>
    public DigitalBrainOptions WithVectorDatabase(string provider, string id = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lastModelRegistration = ModelRegistry.Register(
            new DigitalBrainModelDescriptor(DigitalBrainCapabilityKind.VectorDatabase, provider, id, id, DigitalBrainModelCapabilities.FullyCapable),
            DigitalBrainModelRole.Default);
        return this;
    }

    /// <summary>
    /// Marks the most recently registered model as the fast LLM route.
    /// </summary>
    public DigitalBrainOptions AsFast() => SetLastModelRole(DigitalBrainModelRole.Fast);

    /// <summary>
    /// Marks the most recently registered model as the balanced/default LLM route.
    /// </summary>
    public DigitalBrainOptions AsBalanced() => SetLastModelRole(DigitalBrainModelRole.Balanced);

    /// <summary>
    /// Marks the most recently registered model as the reasoning LLM route.
    /// </summary>
    public DigitalBrainOptions AsReasoning() => SetLastModelRole(DigitalBrainModelRole.Reasoning);

    private DigitalBrainOptions SetLastModelRole(DigitalBrainModelRole role)
    {
        if (lastModelRegistration is null)
        {
            throw new InvalidOperationException("Register a model before assigning a routing role.");
        }

        ModelRegistry.SetRole(lastModelRegistration.Value, role);
        return this;
    }

    private void SelectLlm(DigitalBrainModel model)
    {
        llmProvider = model.Provider;
        llmModel = model.Id;
        llmProviderOverridden = false;
        llmModelOverridden = false;
    }
}
