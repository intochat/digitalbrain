using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Core.Models;

namespace DigitalBrain.Aspire;

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
            : "llama3.1:8b");

        IResourceBuilder<ParameterResource>? azureOpenAIEndpoint = null;
        IResourceBuilder<ParameterResource>? azureOpenAIKey = null;
        if (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
        {
            azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint");
            azureOpenAIKey = builder.AddParameter("azure-openai-key", secret: true);
        }

        IResourceBuilder<ParameterResource>? openAIApiKey = null;
        if (string.Equals(llmProvider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase) ||
            options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.OpenAI, StringComparison.OrdinalIgnoreCase)))
        {
            openAIApiKey = builder.AddParameter("openai-api-key", secret: true);
        }

        IResourceBuilder<ParameterResource>? anthropicApiKey = null;
        if (options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.Anthropic, StringComparison.OrdinalIgnoreCase)))
        {
            anthropicApiKey = builder.AddParameter("anthropic-api-key", secret: true);
        }

        IResourceBuilder<ParameterResource>? githubModelsToken = null;
        if (string.Equals(llmProvider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase) ||
            options.ModelRegistry.Registrations.Any(r => string.Equals(r.Model.Provider, DigitalBrainProviderIds.GitHubModels, StringComparison.OrdinalIgnoreCase)))
        {
            githubModelsToken = builder.AddParameter("github-models-token", secret: true);
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

        var defaultLlm = options.ModelRegistry.DefaultLlm?.Model;
        var defaultOllamaLlm = defaultLlm is not null &&
            string.Equals(defaultLlm.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
                ? defaultLlm.Id
                : "llama3.1:8b";
        var defaultEmbedding = options.ModelRegistry.DefaultEmbedding?.Model;
        var defaultOllamaEmbedding = defaultEmbedding is not null &&
            string.Equals(defaultEmbedding.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
                ? defaultEmbedding.Id
                : "mxbai-embed-large";
        IResourceBuilder<IResourceWithConnectionString> llm;
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
            llm = ollama.AddModel("llm", defaultOllamaLlm);
            embeddingModel = ollama.AddModel("embed", defaultOllamaEmbedding);

            // Pre-pull every other distinct Ollama LLM tag in the registry (e.g. Llama31_8B, registered
            // .AsReasoning() for Ino's tool-calling path) into this same container. Without this, a model
            // the registry points Ino at would never actually exist in the running container, and the first
            // real chat call to it would fail with a "model not found" error from Ollama.
            var pulledOllamaModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { defaultOllamaLlm };
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
            // Same container as the LLM resource: Ollama serves every pulled model from one endpoint,
            // selected by model name in the request, not by a per-model endpoint.
            embeddingOllamaEndpoint = ollamaEndpoint;
        }
        else
        {
            llm = builder.AddConnectionString("llm");
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
            Llm = llm,
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
            OpenAIApiKey = openAIApiKey,
            AnthropicApiKey = anthropicApiKey,
            GitHubModelsToken = githubModelsToken,
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
        if (ctx.OpenAIApiKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__OpenAIApiKey", ctx.OpenAIApiKey);
        }
        if (ctx.AnthropicApiKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AnthropicApiKey", ctx.AnthropicApiKey);
        }
        if (ctx.GitHubModelsToken is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__GitHubModelsToken", ctx.GitHubModelsToken);
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
