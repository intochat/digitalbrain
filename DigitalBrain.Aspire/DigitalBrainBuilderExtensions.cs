using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;

namespace DigitalBrain.Aspire;

public sealed class DigitalBrainContext
{
    public required string Name { get; init; }
    public required IDistributedApplicationBuilder ApplicationBuilder { get; init; }
    public required OrleansService Orleans { get; init; }
    public required object Llm { get; init; }
    public required OrleansServiceClient OrleansClient { get; init; }
    public required int KernelReplicas { get; init; }
    public required bool UseLocalMarketplace { get; init; }

    // The resolved LLM model name (e.g. "qwen2.5-coder:1.5b") for env injection
    public required string LlmModel { get; init; }

    // The resolved LLM provider ("ollama" | "azureopenai"), set via DigitalBrainOptions.WithLLM<TModel>()
    public required string LlmProvider { get; init; }

    // Ollama container http endpoint for DigitalBrain__Llm__OllamaEndpoint injection
    public required EndpointReference OllamaEndpoint { get; init; }

    // Set only when LlmProvider is "azureopenai" (WithLLM<TModel>() where TModel.Provider == "azureopenai")
    public IResourceBuilder<ParameterResource>? AzureOpenAIEndpoint { get; init; }
    public IResourceBuilder<ParameterResource>? AzureOpenAIKey { get; init; }

    // Storage resources exposed so AppHost can wire WithReference on silo
    public required IResourceBuilder<AzureBlobStorageResource> GrainBlobs { get; init; }
    public required IResourceBuilder<AzureBlobStorageResource> JournalBlobs { get; init; }
    public required IResourceBuilder<AzureTableStorageResource> ClusteringTable { get; init; }

    // For encapsulated dashboard + MCP (WithOrleansDashboard / WithMcp)
    public bool EnableOrleansDashboard { get; set; }
    public int? OrleansDashboardPort { get; set; }
    public bool EnableMcp { get; set; }
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

        var llmProvider = options.LlmProvider;
        var llmModel = options.LlmModel ?? (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase)
            ? "gpt-4o-mini"
            : "qwen2.5-coder:1.5b");

        IResourceBuilder<ParameterResource>? azureOpenAIEndpoint = null;
        IResourceBuilder<ParameterResource>? azureOpenAIKey = null;
        if (string.Equals(llmProvider, "azureopenai", StringComparison.OrdinalIgnoreCase))
        {
            azureOpenAIEndpoint = builder.AddParameter("azure-openai-endpoint");
            azureOpenAIKey = builder.AddParameter("azure-openai-key", secret: true);
        }

        var storage = builder.AddAzureStorage("storage").RunAsEmulator();
        var clusteringTable = storage.AddTables("clustering");
        var grainBlobs = storage.AddBlobs("grainstate");
        var journalBlobs = storage.AddBlobs("journal");

        var orleans = builder.AddOrleans("kernel")
            .WithClustering(clusteringTable)
            .WithGrainStorage("Default", grainBlobs);

        // Ollama always runs as the offline fallback (per DEMO-PLAN), independent of the chosen primary
        // provider — it must pull its own real model tag, never the primary provider's model/deployment
        // name (e.g. an azureopenai deployment name like "gpt-4o-mini" is not a pullable Ollama tag).
        const string ollamaFallbackModel = "qwen2.5-coder:1.5b";
        var ollama = builder.AddOllama("ollama")
            .WithGPUSupport()
            .WithDataVolume()
            .WithOpenWebUI();
        var qwen = ollama.AddModel("qwen", ollamaFallbackModel);

        return new DigitalBrainContext
        {
            Name = name,
            ApplicationBuilder = builder,
            Orleans = orleans,
            Llm = qwen,
            OrleansClient = orleans.AsClient(),
            KernelReplicas = options.KernelReplicas,
            UseLocalMarketplace = options.UseLocalMarketplace,
            LlmModel = llmModel,
            LlmProvider = llmProvider,
            OllamaEndpoint = ollama.GetEndpoint("http"),
            AzureOpenAIEndpoint = azureOpenAIEndpoint,
            AzureOpenAIKey = azureOpenAIKey,
            EnableOrleansDashboard = options.EnableOrleansDashboard,
            OrleansDashboardPort = options.OrleansDashboardPort,
            EnableMcp = options.EnableMcp,
            GrainBlobs = grainBlobs,
            JournalBlobs = journalBlobs,
            ClusteringTable = clusteringTable
        };
    }

    public static DigitalBrainContext WithOrleansDashboard(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableOrleansDashboard = true;
        if (port.HasValue) ctx.OrleansDashboardPort = port;
        return ctx;
    }

    public static DigitalBrainContext WithMcp(this DigitalBrainContext ctx, int? port = null)
    {
        ctx.EnableMcp = true;
        return ctx;
    }

    /// <summary>
    /// Wires a kernel project with the core kernel features out of the box:
    /// marketplace, dynamic UI surfaces, journals, clustering, LLM, and replica count for HA.
    /// This makes the kernel (company brain) provide built-in capabilities (embodiment, status, tasks, etc.)
    /// immediately when the silo starts.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WireKernelSilo(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        kernel = kernel
            .WithReference(ctx.Orleans)
            .WithReference(ctx.ClusteringTable)
            .WithReference(ctx.GrainBlobs)
            .WithReference(ctx.JournalBlobs)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEndpoint(name: "grpc", scheme: "http", env: "ASPNETCORE_HTTP_PORTS", isProxied: true)
            .WithEndpoint(
                name: "web",
                scheme: "http",
                port: KernelWebPort(ctx.ApplicationBuilder),
                env: "DIGITALBRAIN_WEB_PORT",
                isProxied: true)
            .WithExternalHttpEndpoints()
            .WithReplicas(ctx.KernelReplicas);

        kernel.WithEnvironment("DIGITALBRAIN_USE_LOCAL_MARKETPLACE", ctx.UseLocalMarketplace ? "true" : "false");
        kernel.WithEnvironment("DIGITALBRAIN_SURFACES_ENABLED", "true");

        // LLM for kernel built-ins (INO, status diagnosis, code gen, tasks). Provider/model come from
        // DigitalBrainOptions.WithLLM<TModel>() (see LlmModels.cs) rather than a hardcoded string.
        kernel.WithEnvironment("DigitalBrain__Llm__Provider", ctx.LlmProvider);
        kernel.WithEnvironment("DigitalBrain__Llm__Model", ctx.LlmModel);
        kernel.WithEnvironment("DigitalBrain__Llm__OllamaEndpoint",
            ReferenceExpression.Create($"http://{ctx.OllamaEndpoint.Property(EndpointProperty.Host)}:{ctx.OllamaEndpoint.Property(EndpointProperty.Port)}"));

        if (ctx.AzureOpenAIEndpoint is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AzureOpenAIEndpoint", ctx.AzureOpenAIEndpoint);
        }
        if (ctx.AzureOpenAIKey is not null)
        {
            kernel.WithEnvironment("DigitalBrain__Llm__AzureOpenAIKey", ctx.AzureOpenAIKey);
        }

        if (ctx.EnableOrleansDashboard && ctx.OrleansDashboardPort.HasValue)
        {
            kernel.WithEnvironment("ORLEANS_DASHBOARD_PORT", ctx.OrleansDashboardPort.Value.ToString());
        }

        return kernel;
    }

    public static int KernelWebPort(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Kernel:WebPort"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_KERNEL_WEB_PORT");

        return int.TryParse(configured, out var port) && port > 0
            ? port
            : DefaultKernelWebPort;
    }

    /// <summary>
    /// Flutter as marketplace pack + Aspire integration. Call from AppHost when the Flutter pack (DigitalBrain.UI.AspireFlutter) is installed.
    /// Starts Flutter (windows or web-server) wired to brain for live surfaces/RfwCards. Enables full packing/distribution/reuse of the UI client as a NeuroPack.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddFlutterClient(
        this DigitalBrainContext ctx,
        string name,
        string flutterAppPath,
        string target = "windows")
    {
        var cmd = ctx.ApplicationBuilder.Configuration["DigitalBrain:FlutterCommand"]
            ?? Environment.GetEnvironmentVariable("FLUTTER_COMMAND")
            ?? "flutter";

        return ctx.ApplicationBuilder.AddExecutable(
                name,
                cmd,
                flutterAppPath,
                "run",
                "-d",
                target)
            .WithReference(ctx.OrleansClient)
            .WithReference((IResourceBuilder<IResourceWithConnectionString>)ctx.Llm)
            .WithEnvironment("DIGITALBRAIN_UI_PACK", "DigitalBrain.UI.AspireFlutter")
            .WithEnvironment("DIGITALBRAIN_UI_TIER1_RESTART_REQUIRED", "true");
    }

    /// <summary>
    /// Wires the Telegram transport (<c>DigitalBrain.Telegram.Transport</c>) as an Aspire resource that bridges
    /// Telegram updates to the kernel gateway over gRPC. The transport boots no-op without a bot token, so the
    /// resource can be present from startup and configured later (token supplied at launch or via the in-app flow)
    /// with no AppHost restart.
    /// </summary>
    /// <param name="transport">The transport project, created in the AppHost via <c>AddProject&lt;Projects.DigitalBrain_Telegram_Transport&gt;(name)</c> so the generated <c>Projects.*</c> metadata type resolves.</param>
    /// <param name="kernel">The kernel/gateway resource whose gRPC endpoint the transport calls. Its grpc endpoint is injected as the gateway address.</param>
    /// <param name="botToken">Optional secret parameter carrying the Telegram bot token. When omitted (no token configured), the transport runs idle.</param>
    /// <param name="internalServiceKey">Shared service-to-service secret (same value injected into the kernel) that the transport presents on the secrets-returning <c>GetPackConfig</c> RPC. Server/transport-only — never exposed to the Flutter client config.</param>
    public static IResourceBuilder<ProjectResource> WireTelegramTransport(
        this DigitalBrainContext ctx,
        IResourceBuilder<ProjectResource> transport,
        IResourceBuilder<ProjectResource> kernel,
        IResourceBuilder<ParameterResource>? botToken = null,
        IResourceBuilder<ParameterResource>? internalServiceKey = null)
    {
        var kernelGrpc = kernel.GetEndpoint("grpc");

        transport = transport
            .WithReference(ctx.OrleansClient)
            .WithReference(kernel)
            .WaitFor(kernel)
            .WithEnvironment("DigitalBrain__GatewayAddress",
                ReferenceExpression.Create($"http://{kernelGrpc.Property(EndpointProperty.Host)}:{kernelGrpc.Property(EndpointProperty.Port)}"));

        if (botToken is not null)
        {
            transport = transport.WithEnvironment("Telegram__BotToken", botToken);
        }

        if (internalServiceKey is not null)
        {
            transport = transport.WithEnvironment("DigitalBrain__InternalServiceKey", internalServiceKey);
        }

        // Tell the transport which marketplace pack's stored config carries its bot token.
        // Matches the pack name in MarketplaceSeeds and the ConfigPack constant inside the pack code.
        transport = transport
            .WithEnvironment("Telegram__PackName", "DigitalBrain.Telegram.Responder")
            .WithEnvironment("Telegram__ConfigScope", "default");

        return transport;
    }

    // Dev default helper (item 12). Path resolve + AddFlutterClient + kernel ref.
    // The DigitalBrain.UI.AspireFlutter (or equivalent) pack can later provide/override these resource bits.
    public static IResourceBuilder<ExecutableResource>? AddDefaultDevFlutterClient(this DigitalBrainContext ctx, IResourceBuilder<ProjectResource> kernel)
    {
        var flutterPath = ResolveDevFlutterAppPath(ctx.ApplicationBuilder);
        if (string.IsNullOrEmpty(flutterPath))
            return null;
        return ctx.AddFlutterClient("flutter-ui", flutterPath, "windows")
            .WithReference(kernel);
    }

    // Public so packs / other extensions can reuse the dev path resolution logic or provide alternatives.
    public static string? ResolveDevFlutterAppPath(IDistributedApplicationBuilder b)
    {
        var flutterPathEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_FLUTTER_APP_PATH");
        if (!string.IsNullOrWhiteSpace(flutterPathEnv) && Directory.Exists(flutterPathEnv))
            return Path.GetFullPath(flutterPathEnv);

        var appHostDir = b.AppHostDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(appHostDir, "..", "app")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "app")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "app")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "pubspec.yaml")))
                return c;
        }

        var dir = new System.IO.DirectoryInfo(appHostDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "app");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "pubspec.yaml")))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }
        return null;
    }
}

public sealed class DigitalBrainOptions
{
    public string? LlmModel { get; set; }
    public string LlmProvider { get; set; } = "ollama";
    public int KernelReplicas { get; set; } = 3;
    public bool UseLocalMarketplace { get; set; } = true;

    public bool EnableOrleansDashboard { get; set; } = true;
    public int? OrleansDashboardPort { get; set; } = 8080;
    public bool EnableMcp { get; set; } = true;

    // Typed model selection, e.g. options.WithLLM<Gpt4oMini>() or options.WithLLM<Qwen25Coder1_5B>() —
    // replaces setting LlmModel/LlmProvider as raw strings.
    public DigitalBrainOptions WithLLM<TModel>() where TModel : LlmModel, new()
    {
        var model = new TModel();
        LlmProvider = model.Provider;
        LlmModel = model.Id;
        return this;
    }
}
