namespace DigitalBrain.Hosting.Microsoft.Aspire;

public sealed record Gemma3;
public sealed record Nemotron3Nano;

public sealed class DigitalBrainDomainResource([ResourceName] string name, string worldId) : Resource(name)
{
    public string WorldId { get; } = worldId;
    public string KernelProjectPath { get; set; } = "src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj";
    public string? TargetWorldId { get; set; }
    public List<string> ModelNames { get; } = [];
    public int? SiloPort { get; set; }
    public int? GatewayPort { get; set; }
    public string? AssociatedKernelResourceName { get; set; }
    internal string? PendingTierModel { get; set; }
    internal string? PendingTierKey { get; set; }
    public Dictionary<string, string> TierEnvs { get; } = new();
}

public static class DigitalBrainDomainResourceBuilderExtensions
{
    public static IResourceBuilder<DigitalBrainDomainResource> AddDigitalBrainDomain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? worldId = null)
    {
        worldId ??= name.ToLowerInvariant().Replace(" ", "-");
        var resource = new DigitalBrainDomainResource(name, worldId);
        return builder.AddResource(resource);
    }

    public static IResourceBuilder<DigitalBrainDomainResource> AddDigitalBrain(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string? worldId = null)
    {
        var domainBuilder = builder.AddDigitalBrainDomain(name, worldId);
        var domain = domainBuilder.Resource;
        var world = domain.WorldId ?? "root";
        var portOffset = Math.Abs(world.GetHashCode()) % 200;
        var silo = 11111 + portOffset;
        var gateway = 30000 + portOffset;
        domain.SiloPort = silo;
        domain.GatewayPort = gateway;
        return domainBuilder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithWorldId(this IResourceBuilder<DigitalBrainDomainResource> builder, string worldId)
    {
        builder.Resource.TargetWorldId = worldId;
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithModels(this IResourceBuilder<DigitalBrainDomainResource> builder, params IResourceBuilder<IResource>[] models)
    {
        foreach (var m in models)
        {
            if (m?.Resource?.Name is { } n && !builder.Resource.ModelNames.Contains(n))
                builder.Resource.ModelNames.Add(n);
        }
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithPorts(this IResourceBuilder<DigitalBrainDomainResource> builder, int siloPort, int gatewayPort)
    {
        builder.Resource.SiloPort = siloPort;
        builder.Resource.GatewayPort = gatewayPort;
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithKernelProject(this IResourceBuilder<DigitalBrainDomainResource> builder, string projectPath = "src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj")
    {
        builder.Resource.KernelProjectPath = projectPath;
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithKernel(this IResourceBuilder<DigitalBrainDomainResource> builder, IResourceBuilder<IResourceWithEnvironment> kernelProject)
    {
        if (kernelProject?.Resource?.Name is { } name && !string.IsNullOrWhiteSpace(name))
            builder.Resource.AssociatedKernelResourceName = name;
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithLlm<TModel>(this IResourceBuilder<DigitalBrainDomainResource> builder) where TModel : notnull
    {
        var model = typeof(TModel).Name switch
        {
            nameof(Gemma3) => "gemma3:1b",
            nameof(Nemotron3Nano) => "nemotron-3-nano",
            _ => "gemma3:1b"
        };
        builder.Resource.PendingTierModel = model;
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> AsFast(this IResourceBuilder<DigitalBrainDomainResource> builder)
    { SetPendingTier(builder, "fast"); return builder; }
    public static IResourceBuilder<DigitalBrainDomainResource> AsBalanced(this IResourceBuilder<DigitalBrainDomainResource> builder)
    { SetPendingTier(builder, "balanced"); return builder; }
    public static IResourceBuilder<DigitalBrainDomainResource> AsReasoning(this IResourceBuilder<DigitalBrainDomainResource> builder)
    { SetPendingTier(builder, "reasoning"); return builder; }

    public static IResourceBuilder<DigitalBrainDomainResource> WithBundle<TBundle>(this IResourceBuilder<DigitalBrainDomainResource> builder) => builder;
    public static IResourceBuilder<DigitalBrainDomainResource> AsSilo(this IResourceBuilder<DigitalBrainDomainResource> builder) { builder.Resource.PendingTierKey = "AsSilo"; return builder; }
    public static IResourceBuilder<DigitalBrainDomainResource> WithVoiceToText<T>(this IResourceBuilder<DigitalBrainDomainResource> builder) => builder;
    public static IResourceBuilder<DigitalBrainDomainResource> WithDurability(this IResourceBuilder<DigitalBrainDomainResource> builder, Action<object> configure) => builder;
    public static IResourceBuilder<DigitalBrainDomainResource> WithUI(this IResourceBuilder<DigitalBrainDomainResource> builder, Action<object> configure) => builder;

    private static void SetPendingTier(IResourceBuilder<DigitalBrainDomainResource> builder, string tier)
    {
        if (builder.Resource.PendingTierModel is { } m)
            builder.Resource.TierEnvs[$"DIGITALBRAIN_LLM_{tier.ToUpperInvariant()}"] = m;
    }
}

internal static class Compute
{
    public static (int session, int portOffset, string clusterId, int silo, int gateway, string kernelName) ComputeKernelParams(DigitalBrainDomainResource domain)
    {
        var world = domain.WorldId ?? "root";
        var offset = world.GetHashCode(StringComparison.Ordinal) & 0xFF;
        return (0, offset, $"db-{world}", 11111 + offset, 30000 + offset, $"kernel-{world}");
    }
}

public static class DigitalBrainDomainResourceBuilderExtensions2
{
    public static IResourceBuilder<ProjectResource> AddKernel(this IResourceBuilder<DigitalBrainDomainResource> domainBuilder, params IResourceBuilder<IResourceWithConnectionString>[] models)
    {
        var appBuilder = domainBuilder.ApplicationBuilder;
        var domain = domainBuilder.Resource;
        var (session, portOffset, clusterId, silo, gateway, kernelName) = Compute.ComputeKernelParams(domain);
        var projectPath = domain.KernelProjectPath;
        var kernel = appBuilder.AddProject(kernelName, projectPath)
            .WithEnvironment("DIGITALBRAIN_WORLD_ID", domain.WorldId)
            .WithEnvironment("DIGITALBRAIN_CLUSTER_ID", clusterId)
            .WithEnvironment("DIGITALBRAIN_SERVICE_ID", "digitalbrain")
            .WithEnvironment("DIGITALBRAIN_SILO_PORT", silo.ToString())
            .WithEnvironment("DIGITALBRAIN_GATEWAY_PORT", gateway.ToString())
            .WithEnvironment("Logging__LogLevel__Default", "Warning")
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        foreach (var m in models) if (m is not null) kernel = kernel.WithReference(m);
        var surfaceHttp = 8080 + portOffset;
        kernel = kernel.WithHttpEndpoint(surfaceHttp, name: "http");
        domainBuilder.WithKernel(kernel);
        kernel = kernel.WithHttpHealthCheck("/health");
        return kernel;
    }

    // The single entry used by AppHost + ino.cs
    public static IDistributedApplicationBuilder AddDefaultDigitalBrainTopology(this IDistributedApplicationBuilder builder)
        => builder.AddMinimalDigitalBrain();

    public static IDistributedApplicationBuilder AddMinimalDigitalBrain(this IDistributedApplicationBuilder builder)
    {
        // Local Ollama with GPU support + persistent data volume for models (large downloads ~5min first time for 26b-class).
        // Matches the IAW AppHost pattern: .WithGPUSupport().WithDataVolume()
        // Ollama local with data volume (model cache survives restarts) + GPU support when host has NVIDIA toolkit / WSL CUDA.
        // Large 26b+ models can take ~5 minutes first download.
        var ollama = builder.AddOllama("ollama")
            .WithGPUSupport()
            .WithDataVolume()
            .WithHttpEndpoint(11434, 11434, "http")
            .WithLifetime(ContainerLifetime.Persistent)
            .WithOpenWebUI(ui => ui.WithLifetime(ContainerLifetime.Persistent))
            .WithImage("ollama/ollama:latest");  // use latest Ollama to support gemma4:26b model manifest (avoids 412 error)

        // Expressive large local model (Gemma4_26b marker for type, tag gemma4:26b).
        // Pinned Ollama image 0.5.4+ to support the model manifest.
        var gemma = ollama.AddModel("gemma4-26b", "gemma4:26b");
        // Small fast model also available for dev speed
        _ = ollama.AddModel("gemma-fast", "gemma3:1b");

        var useRedis = string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_DURABILITY"), "redis", StringComparison.OrdinalIgnoreCase);
        var orleansRedis = useRedis ? builder.AddRedis("orleans-redis").WithDataVolume() : null;

        var aspireDashboardUrl = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_URL")
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL")
            ?? "http://localhost:18888/login";

        static string GetAbsolute(string relative)
        {
            var cwd = Directory.GetCurrentDirectory();
            var d = new DirectoryInfo(cwd);
            int maxUp = 20;
            while (d is not null && maxUp-- > 0)
            {
                if (File.Exists(Path.Combine(d.FullName, "Directory.Packages.props")) || File.Exists(Path.Combine(d.FullName, "brain.ino")) || File.Exists(Path.Combine(d.FullName, "DigitalBrain.slnx")))
                    return Path.GetFullPath(Path.Combine(d.FullName, relative));
                d = d.Parent;
            }
            return Path.GetFullPath(Path.Combine(cwd, "..", "..", relative));
        }

        var rootDomain = builder.AddDigitalBrain("digitalbrain", worldId: "root")
            .WithKernelProject(GetAbsolute("src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj"));
        rootDomain = rootDomain.WithModels(gemma);
        var rootKernel = rootDomain.AddKernel();
        if (gemma is not null) rootKernel = rootKernel.WithReference(ollama, connectionName: "gemma");
        rootKernel = rootKernel.WithHttpEndpoint(8080, name: "http");
        rootKernel.WithEnvironment("GEMMA_MODEL", "gemma4:26b");
        rootKernel.WithEnvironment("DIGITALBRAIN_LLM_DEFAULT", "gemma4:26b");
        rootKernel.WithEnvironment("DIGITALBRAIN_BRAIN_NAME", "self");
        rootKernel.WithEnvironment("DIGITALBRAIN_DURABILITY", useRedis ? "redis" : "memory");
        if (orleansRedis is not null) rootKernel = rootKernel.WithReference(orleansRedis);
        rootKernel.WithEnvironment("DIGITALBRAIN_DASHBOARD_URL", aspireDashboardUrl);
        rootKernel.WithEnvironment("DOTNET_ENVIRONMENT", "Development");
        rootKernel.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        rootKernel.WithEnvironment("DIGITALBRAIN_SEED_CAPSULES", "demo");

        rootKernel.WithCommand("rebuild-silo", "Rebuild Kernel Silo", _ =>
            Task.FromResult(new ExecuteCommandResult { Success = true }));

        rootKernel.WithCommand("fire-demo", "Fire Demo Tap (headless mcp test for synapse log)", async _ =>
        {
            // Direct Send via kernel /fire-demo endpoint (headless/mcp without browser/flutter)
            try { using var hc = new System.Net.Http.HttpClient(); await hc.PostAsync("http://localhost:8080/fire-demo", null); } catch { }
            return new ExecuteCommandResult { Success = true };
        });

        // Flutter web for interactive DEMO (headline E2E). Explicit start so core aspire run (kernel+ollama+redis) is fast and does not require Flutter SDK on host.
        // Use: aspire resource flutter-web start  (or dashboard start button), then browser :5801, press DEMO.
        // Matches official: AddExecutable + WithHttpEndpoint + WithExplicitStart (see aspire docs host-external-executables).
        var flutterWeb = builder.AddExecutable(
                "flutter-web",
                "flutter",
                "src/DigitalBrain.Clients.Flutter",
                "run", "-d", "web-server", "--web-port=5801", "--web-hostname=0.0.0.0",
                "--dart-define=KERNEL_GRPC_HOST=localhost", "--dart-define=KERNEL_GRPC_PORT=8080")
            .WithEnvironment("DIGITALBRAIN_SURFACE_GRPC", () => rootKernel.GetEndpoint("http").ToString())
            .WithEnvironment("KERNEL_GRPC_HOST", "localhost")
            .WithEnvironment("KERNEL_GRPC_PORT", "8080")
            .WithEnvironment("ASPIRE_DASHBOARD_URL", aspireDashboardUrl)
            .WithEnvironment("_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS", "1")
            .WithHttpEndpoint(5801, name: "http", isProxied: false)
            .WithExplicitStart();
        return builder;
    }

    public static IDistributedApplicationBuilder AddDigitalBrainManifest(this IDistributedApplicationBuilder builder, object? _ = null)
        => builder.AddMinimalDigitalBrain();
}

internal static class DigitalBrainTierEnv
{
    public const string Fast = "DIGITALBRAIN_LLM_FAST";
    public const string Balanced = "DIGITALBRAIN_LLM_BALANCED";
    public const string Reasoning = "DIGITALBRAIN_LLM_REASONING";
}

public static class DigitalBrainInoHost
{
    public static void Run(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        builder.AddMinimalDigitalBrain();
        builder.Build().Run();
    }
}

internal static class ComputeKernelParamsHelper
{
    public static (int session, int portOffset, string clusterId, int silo, int gateway, string kernelName) ComputeKernelParams(DigitalBrainDomainResource domain)
    {
        var world = domain.WorldId ?? "root";
        var offset = Math.Abs(world.GetHashCode()) % 200;
        return (0, offset, $"db-{world}", 11111 + offset, 30000 + offset, $"kernel-{world}");
    }
}
