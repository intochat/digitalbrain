using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using DigitalBrain.Kernel.Contracts.Models;
namespace DigitalBrain.AppHost;

internal static class DigitalBrainBuilderExtensions
{
    public const int DefaultKernelWebPort = 51014;
    public const string DefaultRuntimeStorageNamespace = "main";
    private const string ConversationStateProvider = "runtime-conversations";
    private const string SurfaceFeedStateProvider = "runtime-surface-feeds";
    private const string SessionStateProvider = "runtime-sessions";
    public static DigitalBrainContext AddDigitalBrain(this IDistributedApplicationBuilder builder, [ResourceName] string name = "digitalbrain", Action<DigitalBrainOptions>? configure = null)
    {
        var options = new DigitalBrainOptions();
        configure?.Invoke(options);
        if (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS"), out var replicaOverride) && replicaOverride > 0)
        {
            options.KernelReplicas = replicaOverride;
        }
        var llmProvider = options.ResolvedLlmProvider;
        var llmModel = options.ResolvedLlmModel ?? (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase) ? "gpt-4o-mini" : "llama3.1:8b");
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
        var isRunMode = builder.ExecutionContext.IsRunMode;
        var runtimeStorageNamespace = ResolveRuntimeStorageNamespace(builder.Configuration["DigitalBrain:Runtime:StorageNamespace"]);
        var storage = builder.AddAzureStorage(isRunMode ? "runtime-storage" : "storage");
        if (isRunMode)
        {
            storage.RunAsEmulator(azurite =>
            {
                azurite.WithDataVolume($"{name}-{runtimeStorageNamespace}-azurite-data").WithLifetime(ContainerLifetime.Persistent);
            });
        }
        var clusteringTable = storage.AddTables("clustering");
        var memoryFacts = storage.AddTables("memoryfacts");
        var featureArtifacts = storage.AddBlobs("features");
        var grainBlobs = storage.AddBlobs("grainstate");
        var conversationStateBlobs = storage.AddBlobs("conversationstate");
        var surfaceFeedStateBlobs = storage.AddBlobs("surfacefeedstate");
        var sessionStateBlobs = storage.AddBlobs("sessionstate");
        var orleans = builder.AddOrleans("kernel").WithClustering(clusteringTable).WithGrainStorage("Default", grainBlobs).WithGrainStorage(ConversationStateProvider, conversationStateBlobs)
            .WithGrainStorage(SurfaceFeedStateProvider, surfaceFeedStateBlobs)
            .WithGrainStorage(SessionStateProvider, sessionStateBlobs)
            .WithReminders(clusteringTable);
        if (isRunMode)
        {
            orleans.WithClusterId(ResolveLocalClusterId());
        }
        var defaultLlm = options.ModelRegistry.DefaultLlm?.Model;
        var defaultOllamaLlm = defaultLlm is not null && string.Equals(defaultLlm.Provider, DigitalBrainProviderIds.Ollama, StringComparison.OrdinalIgnoreCase)
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
            var ollama = builder.AddOllama("ollama").WithGPUSupport().WithDataVolume().WithLifetime(ContainerLifetime.Persistent).WithOpenWebUI(webui => webui.WithLifetime(ContainerLifetime.Persistent).WithDataVolume());
            llm = ollama.AddModel("llm", defaultOllamaLlm);
            embeddingModel = ollama.AddModel("embed", defaultOllamaEmbedding);
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
            embeddingOllamaEndpoint = ollamaEndpoint;
        }
        else
        {
            llm = builder.AddConnectionString("llm");
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
            AzureOpenAIEndpoint = azureOpenAIEndpoint,
            AzureOpenAIKey = azureOpenAIKey,
            OpenAIApiKey = openAIApiKey,
            AnthropicApiKey = anthropicApiKey,
            GitHubModelsToken = githubModelsToken,
            EnableMcp = options.EnableMcp,
            GrainBlobs = grainBlobs,
            ConversationStateBlobs = conversationStateBlobs,
            SurfaceFeedStateBlobs = surfaceFeedStateBlobs,
            SessionStateBlobs = sessionStateBlobs,
            ClusteringTable = clusteringTable,
            MemoryFacts = memoryFacts,
            FeatureArtifacts = featureArtifacts
        };
    }
    public static IResourceBuilder<ProjectResource> ConfigureServer(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        kernel = kernel.WithReference(ctx.Orleans).WithReference(ctx.ClusteringTable).WithReference(ctx.MemoryFacts).WithReference(ctx.FeatureArtifacts)
            .WithReference(ctx.GrainBlobs)
            .WithReference(ctx.ConversationStateBlobs)
            .WithReference(ctx.SurfaceFeedStateBlobs)
            .WithReference(ctx.SessionStateBlobs)
            .WithReference(ctx.Llm)
            .WithEndpoint(name: "grpc", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithEndpoint(name: "web", scheme: "http", port: KernelWebPort(ctx.ApplicationBuilder), env: "DIGITALBRAIN_WEB_PORT", isProxied: true)
            .WithExternalHttpEndpoints()
            .WithReplicas(ctx.KernelReplicas);
        kernel.WaitFor(ctx.ClusteringTable);
        kernel.WaitFor(ctx.MemoryFacts);
        kernel.WaitFor(ctx.FeatureArtifacts);
        kernel.WaitFor(ctx.GrainBlobs);
        kernel.WaitFor(ctx.ConversationStateBlobs);
        kernel.WaitFor(ctx.SurfaceFeedStateBlobs);
        kernel.WaitFor(ctx.SessionStateBlobs);
        kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");
        if (TryResolveRepositoryRoot(out var repositoryRoot))
            kernel.WithEnvironment("DIGITALBRAIN_REPO_ROOT", repositoryRoot);
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
        return kernel;
    }
    public static IResourceBuilder<ProjectResource> ConfigureClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> client)
    {
        return client.WithReference(ctx.OrleansClient);
    }
    private static ReferenceExpression HttpUrl(EndpointReference endpoint, string pathSuffix = "") =>
        ReferenceExpression.Create($"http://{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}{pathSuffix}");
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
    private static void WithOptionalEnvironment(this IResourceBuilder<ProjectResource> resource, string configurationKey, string environmentKey, string targetKey)
    {
        var value = resource.ApplicationBuilder.Configuration[configurationKey] ?? Environment.GetEnvironmentVariable(environmentKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            resource.WithEnvironment(targetKey, value);
        }
    }
    public static int KernelWebPort(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Kernel:WebPort"] ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_WEB_PORT");
        return int.TryParse(configured, out var port) && port > 0 ? port : DefaultKernelWebPort;
    }
    internal static string ResolveRuntimeStorageNamespace(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? DefaultRuntimeStorageNamespace : configured.Trim().ToLowerInvariant();
        if (value.Length > 48 || !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException(
                "DigitalBrain:Runtime:StorageNamespace must start with an ASCII letter or digit and contain at most 48 ASCII letters, digits, hyphens, underscores, or periods.");
        }
        return value;
    }
    internal static string ResolveLocalClusterId() => ResolveLocalClusterId(Environment.GetEnvironmentVariable);
    internal static string ResolveLocalClusterId(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        return getEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID")
            ?? getEnvironmentVariable("DigitalBrain__ClusterId")
            ?? getEnvironmentVariable("Orleans__ClusterId") ?? $"digitalbrain-dev-{Guid.NewGuid():N}";
    }

    private static bool TryResolveRepositoryRoot(out string root)
    {
        var configured = Environment.GetEnvironmentVariable("DIGITALBRAIN_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "Brain.slnx")))
        {
            root = Path.GetFullPath(configured);
            return true;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            {
                root = directory.FullName;
                return true;
            }

            directory = directory.Parent;
        }

        root = string.Empty;
        return false;
    }
}
