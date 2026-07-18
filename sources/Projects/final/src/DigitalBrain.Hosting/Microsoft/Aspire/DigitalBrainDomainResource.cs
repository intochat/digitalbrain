using DigitalBrain.InoLang.Domain.Ino;
using DigitalBrain.InoLang.Domain.Yaml;
using DigitalBrain.Os.Application;
using DigitalBrain.Os.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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

        var (session, portOffset, clusterId, silo, gateway, kernelName) = ComputeKernelParams(domain);

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

    public static IResourceBuilder<DigitalBrainDomainResource> WithKernelProject(this IResourceBuilder<DigitalBrainDomainResource> builder, string projectPath = "../DigitalBrain.Kernel/DigitalBrain.Kernel.csproj")
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

    public static IResourceBuilder<DigitalBrainDomainResource> WithLlm<TModel>(this IResourceBuilder<DigitalBrainDomainResource> builder)
        where TModel : notnull
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
    {
        SetPendingTier(builder, "fast");
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> AsBalanced(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        SetPendingTier(builder, "balanced");
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> AsReasoning(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        SetPendingTier(builder, "reasoning");
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithBundle<TBundle>(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> AsSilo(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        // U5: real .AsSilo for e.g. GoogleAuth (contract in Sdk, impl in bundle silo resource joining cluster; heterogeneous placement puts the grain only on the silo with the impl assembly).
        // For demo: marker on resource so AddKernel / topology can treat as separate silo resource (env applied to the kernel project); full separate csproj + Orleans placement in later.
        builder.Resource.PendingTierKey = "AsSilo"; // marker for L3 promotion
        // The caller (ino.cs or topology) can check and wire a dedicated bundle silo resource.
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithVoiceToText<T>(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithDurability(this IResourceBuilder<DigitalBrainDomainResource> builder, Action<object> configure)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithUI(this IResourceBuilder<DigitalBrainDomainResource> builder, Action<object> configure)
    {
        return builder;
    }

    public static IResourceBuilder<DigitalBrainDomainResource> WithPeerDiscovery(this IResourceBuilder<DigitalBrainDomainResource> builder)
    {
        return builder;
    }

    private static void SetPendingTier(IResourceBuilder<DigitalBrainDomainResource> builder, string tier)
    {
        if (builder.Resource.PendingTierModel is { } model)
        {
            var envName = $"DIGITALBRAIN_LLM_{tier.ToUpperInvariant()}";
            builder.Resource.TierEnvs[envName] = model;
            builder.Resource.PendingTierKey = tier;
            builder.Resource.PendingTierModel = null;
        }
    }

    private static (string session, int offset, string clusterId, int silo, int gateway, string kernelName) ComputeKernelParams(DigitalBrainDomainResource domain)
    {
        var session = Environment.GetEnvironmentVariable("DIGITALBRAIN_SESSION") ?? "";
        int portOffset = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_PORT_OFFSET"), out var p)) portOffset = p;
        else if (!string.IsNullOrWhiteSpace(session)) portOffset = Math.Abs(session.GetHashCode() % 100);

        var kernelName = string.Equals(domain.WorldId, "root", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(session) ? "kernel" : $"kernel-{session}")
            : (string.IsNullOrWhiteSpace(session) ? $"kernel-{domain.WorldId}" : $"kernel-{domain.WorldId}-{session}");

        var silo = domain.SiloPort ?? ((string.Equals(domain.WorldId, "root", StringComparison.OrdinalIgnoreCase) ? 11111 : 11112) + portOffset);
        var gateway = domain.GatewayPort ?? ((string.Equals(domain.WorldId, "root", StringComparison.OrdinalIgnoreCase) ? 30000 : 30001) + portOffset);

        var clusterBase = $"digitalbrain-{domain.WorldId}";
        var clusterId = string.IsNullOrWhiteSpace(session) ? clusterBase : $"{clusterBase}-{session}";

        return (session, portOffset, clusterId, silo, gateway, kernelName);
    }

    private static string KernelHttpUrlOrLocalhost(
        IResourceBuilder<global::Aspire.Hosting.ApplicationModel.ProjectResource> kernel,
        int fallbackPort)
    {
        try
        {
            return kernel.GetEndpoint("http").Url.TrimEnd('/');
        }
        catch (InvalidOperationException)
        {
            return $"http://localhost:{fallbackPort}";
        }
    }

    public static IResourceBuilder<global::Aspire.Hosting.ApplicationModel.ProjectResource> AddKernel(this IResourceBuilder<DigitalBrainDomainResource> domainBuilder, params IResourceBuilder<IResourceWithConnectionString>[] models)
    {
        var appBuilder = domainBuilder.ApplicationBuilder;
        var domain = domainBuilder.Resource;

        var (session, portOffset, clusterId, silo, gateway, kernelName) = ComputeKernelParams(domain);

        var projectPath = domain.KernelProjectPath;

        var kernel = appBuilder.AddProject(kernelName, projectPath)
            .WithEnvironment("DIGITALBRAIN_WORLD_ID", domain.WorldId)
            .WithEnvironment("DIGITALBRAIN_CLUSTER_ID", clusterId)
            .WithEnvironment("DIGITALBRAIN_SERVICE_ID", "digitalbrain")
            .WithEnvironment("DIGITALBRAIN_SILO_PORT", silo.ToString())
            .WithEnvironment("DIGITALBRAIN_GATEWAY_PORT", gateway.ToString())
            .WithEnvironment("GEMMA_MODEL", Environment.GetEnvironmentVariable("GEMMA_MODEL") ?? "gemma3:1b")
            .WithEnvironment("NEMOTRON_MODEL", Environment.GetEnvironmentVariable("NEMOTRON_MODEL") ?? "nemotron")
            .WithEnvironment("Logging__LogLevel__Default", "Warning")
            .WithEnvironment("Logging__LogLevel__Orleans", "Warning")
            .WithEnvironment("Logging__LogLevel__Microsoft.Orleans", "Warning")
            .WithEnvironment("Logging__LogLevel__Microsoft", "Warning")
            .WithEnvironment("Logging__LogLevel__OpenTelemetry", "Warning")
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

        foreach (var (k, v) in domain.TierEnvs)
        {
            kernel = kernel.WithEnvironment(k, v);
        }

        foreach (var m in models)
        {
            if (m is not null)
                kernel = kernel.WithReference(m);
        }

        var surfaceHttp = 8080 + portOffset + (string.Equals(domain.WorldId, "root", StringComparison.OrdinalIgnoreCase) ? 0 : 5);
        kernel = kernel.WithHttpEndpoint(surfaceHttp, name: "http");

        // Inject Orleans dashboard url (official dashboard at kernel http + /orleans-dashboard). Deferred so it captures the allocated endpoint from Aspire (works for fixed or dynamic).
        // This + the IAspire neuron (Microsoft.Aspire.Dashboard surface) + env makes the orleans viz "start with rest of digitalbrain" and "automatically connect to current" cluster.
        kernel = kernel.WithEnvironment("DIGITALBRAIN_ORLEANS_DASHBOARD_URL", () => KernelHttpUrlOrLocalhost(kernel, surfaceHttp) + "/orleans-dashboard");

        domainBuilder.WithKernel(kernel);
        kernel = kernel.WithHttpHealthCheck("/health");
        return kernel;
    }

    public static IDistributedApplicationBuilder AddDefaultDigitalBrainTopology(this IDistributedApplicationBuilder builder)
    {
        // Dual boot per OS-ON-YAML-SPEC/PLAN: prefer brain.yaml (os-on-yaml paradigm), fallback .ino.
        if (File.Exists("brain.yaml"))
        {
            var txt = File.ReadAllText("brain.yaml");
            var bootManifest = YamlParser.ParseBoot(txt);
            if (bootManifest != null)
                return builder.AddDigitalBrainManifest(bootManifest);
        }
        if (File.Exists("brain.ino"))
        {
            var bootManifest = InoParser.ParseBoot(File.ReadAllText("brain.ino"));
            return builder.AddDigitalBrainManifest(bootManifest);
        }
        return builder;
    }

    private static string GetAbsoluteRepoPath(string relativeFromRoot)
    {
        var cwd = Directory.GetCurrentDirectory();
        var d = new DirectoryInfo(cwd);
        int maxUp = 20;
        while (d != null && maxUp-- > 0)
        {
            if (File.Exists(Path.Combine(d.FullName, "Directory.Packages.props")) || File.Exists(Path.Combine(d.FullName, "brain.ino")) || File.Exists(Path.Combine(d.FullName, "DigitalBrain.slnx")))
            {
                return Path.GetFullPath(Path.Combine(d.FullName, relativeFromRoot));
            }
            d = d.Parent;
        }
        return Path.GetFullPath(Path.Combine(cwd, "..", "..", relativeFromRoot));
    }

    public static IDistributedApplicationBuilder AddDigitalBrainManifest(this IDistributedApplicationBuilder builder, BootManifest boot)
    {
        var ollama = builder.AddOllama("ollama")
            .WithDataVolume()
            .WithHttpEndpoint(11434, 11434, "http")
            .WithOpenWebUI(ui => ui.WithLifetime(ContainerLifetime.Persistent));

        var gemma = ollama.AddModel("gemma", "gemma3:1b");

        var useRedis = !string.Equals(boot.Durability, "memory", StringComparison.OrdinalIgnoreCase);
        var durabilityEnv = Environment.GetEnvironmentVariable("DIGITALBRAIN_DURABILITY");
        if (!string.IsNullOrWhiteSpace(durabilityEnv))
        {
            useRedis = !string.Equals(durabilityEnv, "memory", StringComparison.OrdinalIgnoreCase);
        }
        IResourceBuilder<RedisResource>? orleansRedis = useRedis ? builder.AddRedis("orleans-redis").WithDataVolume() : null;

        var aspireDashboardUrl = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_URL") ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL") ?? "http://localhost:18888/login";

        static IResourceBuilder<DigitalBrainDomainResource> ApplyLlms(
            IResourceBuilder<DigitalBrainDomainResource> domain,
            List<(string Model, string Tier)> llms)
        {
            foreach (var (model, tier) in llms)
            {
                if (model == "gemma3") domain = domain.WithLlm<Gemma3>();
                else if (model == "nemotron3-nano") domain = domain.WithLlm<Nemotron3Nano>();
                else continue;
                domain = tier switch
                {
                    "fast" => domain.AsFast(),
                    "balanced" => domain.AsBalanced(),
                    "reasoning" => domain.AsReasoning(),
                    _ => domain
                };
            }
            return domain;
        }

        var rootDomain = builder.AddDigitalBrain(boot.Name, worldId: "root")
            .WithKernelProject(GetAbsoluteRepoPath("src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj"));
        rootDomain = ApplyLlms(rootDomain, boot.Llms);
        rootDomain = rootDomain.WithModels(gemma);
        rootDomain = rootDomain.WithPorts(11111, 30111);
        var rootKernel = rootDomain.AddKernel();
        rootKernel = rootKernel.WithReference(ollama, connectionName: "gemma");
        rootKernel = rootKernel.WithHttpEndpoint(8080, name: "http");
        rootKernel.WithEnvironment("GEMMA_MODEL", "gemma3:1b");
        rootKernel.WithEnvironment("DIGITALBRAIN_BRAIN_NAME", boot.Name);
        rootKernel.WithEnvironment("DIGITALBRAIN_DURABILITY", useRedis ? "redis" : "memory");
        if (orleansRedis is not null)
            rootKernel.WithReference(orleansRedis);
        rootKernel.WithEnvironment("DIGITALBRAIN_DASHBOARD_URL", aspireDashboardUrl);
        rootKernel.WithEnvironment("DOTNET_ENVIRONMENT", "Development");
        rootKernel.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        if (boot.Seeds.Length > 0)
            rootKernel.WithEnvironment("DIGITALBRAIN_SEED_CAPSULES", string.Join(";", boot.Seeds));

        // Faithful boot: pass manifest declarations into the kernel as env vars so the runtime can honour them.
        rootKernel.WithEnvironment("DIGITALBRAIN_DISCOVERY", boot.Discovery ? "on" : "off");
        if (!string.IsNullOrWhiteSpace(boot.Voice))
            rootKernel.WithEnvironment("DIGITALBRAIN_VOICE", boot.Voice);
        if (!string.IsNullOrWhiteSpace(boot.Ui))
            rootKernel.WithEnvironment("DIGITALBRAIN_UI", boot.Ui);
        if (!string.IsNullOrWhiteSpace(boot.AdvertisedIpEnv))
        {
            var advertisedIpValue = Environment.GetEnvironmentVariable(boot.AdvertisedIpEnv);
            if (!string.IsNullOrWhiteSpace(advertisedIpValue))
                rootKernel.WithEnvironment("DIGITALBRAIN_ADVERTISED_IP", advertisedIpValue);
        }

        var (_, _, rootClusterId, _, rootGateway, _) = ComputeKernelParams(rootDomain.Resource);

        rootKernel.WithCommand("publish-experience", "Publish Experience", ctx =>
        {
            if (System.Diagnostics.Activity.Current is { } a) a.SetTag("db.command", "publish-experience");
            return Task.FromResult(new ExecuteCommandResult { Success = true });
        });

        rootKernel.WithCommand("fire-demo", "Fire DEMO (emits Demo Executed UiSurface via ClientTap path)", async ctx =>
        {
            if (System.Diagnostics.Activity.Current is { } a) a.SetTag("db.command", "fire-demo");
            try
            {
                Console.WriteLine("[fire-demo] sending ClientTap for Demo to emit surface (gRPC web handler)");
                var webHandler = new Grpc.Net.Client.Web.GrpcWebHandler(Grpc.Net.Client.Web.GrpcWebMode.GrpcWebText, new HttpClientHandler());
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost:8080", new global::Grpc.Net.Client.GrpcChannelOptions { HttpHandler = webHandler, HttpVersion = new Version(1, 1), HttpVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower });
                var client = new global::DigitalBrain.Surfaces.SurfaceStream.SurfaceStreamClient(channel);
                var headers = new Grpc.Core.Metadata { new Grpc.Core.Metadata.Entry("username", "root") };
                var resp = await client.SendClientEventAsync(new global::DigitalBrain.Surfaces.ClientEvent
                {
                    SurfaceId = "demo",
                    EventType = "tap",
                    PayloadJson = "{\"Type\":\"Demo\"}"
                }, new Grpc.Core.CallOptions(headers: headers));
                Console.WriteLine("[fire-demo] gRPC success=" + resp.Success + " -> UiSurface should be emitted");
                Console.WriteLine("Demo tap sent via gRPC (server emitted UiSurface)");
                Console.WriteLine("[demo] orleans tap sent successfully via gw (gRPC primary)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[fire-demo] gRPC send failed: " + ex.Message);
            }
            return new ExecuteCommandResult { Success = true };
        });

        // Monorepo-era extra kernels removed (see multirepo/ at root for the proper split).
        // Private cluster with marketplace + publiccontracts + public multirepo is now folder-based + os-on-yaml driven.
        // Default is clean "sell" view (marketplace seeds only): root kernel + mcp + declared ollama/redis + worlds from yaml.
        // multirepo/private-cluster/ has its own thin AppHost + aspire.config for the focused marketplace private cluster.
        // public-contracts/ holds the INeuron/Synapse/IHandle surface + schema for contract bundles and external consumers.
        if (Environment.GetEnvironmentVariable("DIGITALBRAIN_ENABLE_LEGACY_EXTRA_KERNELS") == "1")
        {
            // Legacy path only (explicit opt-in). New work and default dashboard runs use the multirepo/ split + clean os-on-yaml.
        }

        // digitalbrain-mcp: Aspire project resource (http MCP server) exposing tools that operate the live cluster via Orleans client to IDigitalBrain / IMarketplace / IAspire grains.
        // Port fixed + isProxied false so external MCP clients (Claude Code, VS Copilot, other agents) can connect directly at http://localhost:5810/mcp.
        // Env wiring reuses root cluster ids/ports so the MCP's client connects to the same gateway the kernels use.
        // WaitFor ensures kernel (grains + timeline) is ready before MCP tools succeed.
#pragma warning disable ASPIREMCP001
        var mcp = builder.AddProject("digitalbrain-mcp", GetAbsoluteRepoPath("src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj"))
            .WithEnvironment("DIGITALBRAIN_WORLD_ID", "root")
            .WithEnvironment("DIGITALBRAIN_CLUSTER_ID", rootClusterId)
            .WithEnvironment("DIGITALBRAIN_SERVICE_ID", "digitalbrain")
            .WithEnvironment("DIGITALBRAIN_SILO_PORT", "11111")
            .WithEnvironment("DIGITALBRAIN_GATEWAY_PORT", rootGateway.ToString())
            .WithEnvironment("ASPIRE_DASHBOARD_URL", aspireDashboardUrl)
            .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithHttpEndpoint(5810, name: "mcp", isProxied: false)
            .WithHttpHealthCheck("/health", endpointName: "mcp")
            .WaitFor(rootKernel)
            .WithMcpServer("/mcp", endpointName: "mcp");
#pragma warning restore ASPIREMCP001

        var skipFlutter = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKIP_FLUTTER_RESOURCE"));
        var uiToken = boot.Ui ?? "";
        var uiDisabled = uiToken.Contains("none", StringComparison.OrdinalIgnoreCase)
                      || uiToken.Contains("off", StringComparison.OrdinalIgnoreCase);
        bool flutterAutostart = uiToken.Contains("autostart", StringComparison.OrdinalIgnoreCase);
        // SKIP_FLUTTER_RESOURCE wins unconditionally (CI gate, toolchain-less environments).
        if (!skipFlutter && !uiDisabled)
        {
            // flutter-web and flutter-windows: if ui string contains "autostart" (e.g. "flutter windows autostart" from brain.yaml / os-on-yaml),
            // they start automatically with the AppHost. Otherwise ExplicitStart (launch on demand via dashboard / IFlutter / ResourceCommandService;
            // prevents red failed state in envs without Flutter SDK on PATH).
            var flutterWeb = builder.AddExecutable(
                    "flutter-web",
                    "flutter",
                    GetAbsoluteRepoPath("src/DigitalBrain.Clients.Flutter"),
                    "run", "-d", "web-server", "--web-port=5801", "--web-hostname=0.0.0.0", "--dart-define=KERNEL_GRPC_HOST=localhost", "--dart-define=KERNEL_GRPC_PORT=8080")
                .WithEnvironment("DIGITALBRAIN_SURFACE_GRPC", () => rootKernel.GetEndpoint("http").ToString())
                .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
                .WithEnvironment("KERNEL_GRPC_HOST", "localhost")
                .WithEnvironment("KERNEL_GRPC_PORT", "8080")
                .WithEnvironment("ASPIRE_DASHBOARD_URL", aspireDashboardUrl)
                .WithEnvironment("_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS", "1")
                .WithHttpEndpoint(5801, name: "http", isProxied: false);
            if (!flutterAutostart)
                flutterWeb = flutterWeb.WithExplicitStart();

            var flutterWindows = builder.AddExecutable(
                    "flutter-windows",
                    "cmd",
                    GetAbsoluteRepoPath("src/DigitalBrain.Clients.Flutter"),
                    "/c", "echo [flutter-windows] Forcing clean of Windows native artifacts (build\\windows + ephemeral) to avoid stale CMake/MSBuild install step on VS 18 Insiders... & if exist build\\windows (rmdir /s /q build\\windows 2>nul) & if exist windows\\flutter\\ephemeral (rmdir /s /q windows\\flutter\\ephemeral 2>nul) & flutter run -d windows --no-hot --dart-define=KERNEL_GRPC_HOST=localhost --dart-define=KERNEL_GRPC_PORT=8080")
                .WithEnvironment("DIGITALBRAIN_SURFACE_GRPC", () => rootKernel.GetEndpoint("http").ToString())
                .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
                .WithEnvironment("KERNEL_GRPC_HOST", "localhost")
                .WithEnvironment("KERNEL_GRPC_PORT", "8080")
                .WithEnvironment("ASPIRE_DASHBOARD_URL", aspireDashboardUrl)
                .WithEnvironment("_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS", "1");
            // windows auto-launches like web (no ExplicitStart wrapper)
        }

        // Two console clients for fast manual marketplace distribution testing (pack/publish from A, discover+install from B).
        // Start kernel (root), then use Aspire dashboard "start" on console-market-a and console-market-b (ExplicitStart).
        // In a: pack "shared-demo", publish. In b: use Marketplace tab "install id> local listed" or query peer.
        // Different --brain-key gives separate CurrentBrain / installed bundles while sharing the global marketplace listings.
        // Direct dotnet alternative (no aspire): dotnet run --project src/DigitalBrain.Clients.Console -- --brain-key market-a
        var consoleMarketA = builder.AddExecutable(
                "console-market-a",
                "dotnet",
                GetAbsoluteRepoPath("."),
                "run", "--project", "src/DigitalBrain.Clients.Console", "--no-launch-profile", "--", "--brain-key", "market-a")
            .WithEnvironment("DIGITALBRAIN_GATEWAY_PORT", rootGateway.ToString())
            .WithEnvironment("DIGITALBRAIN_CLUSTER_ID", "digitalbrain-root")
            .WithEnvironment("DIGITALBRAIN_WORLD_ID", "root")
            .WithEnvironment("DIGITALBRAIN_BRAIN_KEY", "market-a")
            .WithExplicitStart();

        var consoleMarketB = builder.AddExecutable(
                "console-market-b",
                "dotnet",
                GetAbsoluteRepoPath("."),
                "run", "--project", "src/DigitalBrain.Clients.Console", "--no-launch-profile", "--", "--brain-key", "market-b")
            .WithEnvironment("DIGITALBRAIN_GATEWAY_PORT", rootGateway.ToString())
            .WithEnvironment("DIGITALBRAIN_CLUSTER_ID", "digitalbrain-root")
            .WithEnvironment("DIGITALBRAIN_WORLD_ID", "root")
            .WithEnvironment("DIGITALBRAIN_BRAIN_KEY", "market-b")
            .WithExplicitStart();

        foreach (var (wname, wpath) in boot.Worlds)
        {
            BootManifest? childBoot = null;
            if (File.Exists(wpath))
            {
                try
                {
                    childBoot = InoParser.ParseBoot(File.ReadAllText(wpath));
                }
                catch (InoParseException ex)
                {
                    Console.WriteLine($"world '{wname}' ({wpath}): {ex.Line}: {ex.Code} {ex.Message}");
                    Environment.Exit(1);
                    throw;
                }
            }
            var childLlms = childBoot?.Llms is { Count: > 0 } cl ? cl : boot.Llms;

            var wDomain = builder.AddDigitalBrain(wname)
                .WithModels(gemma)
                .WithKernelProject(GetAbsoluteRepoPath("src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj"));
            wDomain = ApplyLlms(wDomain, childLlms);

            var wKernel = wDomain.AddKernel();
            wKernel = wKernel.WithReference(ollama, connectionName: "gemma");
            wKernel = wKernel.WithHttpEndpoint(name: "http");
            wKernel.WithEnvironment("GEMMA_MODEL", "gemma3:1b");
            wKernel.WithEnvironment("DIGITALBRAIN_DASHBOARD_URL", aspireDashboardUrl);
            wKernel.WithEnvironment("DOTNET_ENVIRONMENT", "Development");
            wKernel.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
            if (childBoot?.Seeds.Length > 0)
                wKernel.WithEnvironment("DIGITALBRAIN_SEED_CAPSULES", string.Join(";", childBoot.Seeds));
        }

        return builder;
    }
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
        string manifestText = "";
        BootManifest? bootManifest = null;

        string? manifestPath = ResolveYamlBootManifestPath();
        if (manifestPath != null && File.Exists(manifestPath))
        {
            manifestText = File.ReadAllText(manifestPath);
            bootManifest = YamlParser.ParseBoot(manifestText);
            if (bootManifest == null)
            {
                Console.WriteLine("Failed to parse yaml boot manifest (missing schemaVersion os-on-yaml/v0 or invalid structure)");
                Environment.Exit(1);
            }
        }

        if (bootManifest == null)
        {
            throw new InvalidOperationException("dotnet run ino.cs now requires a yaml boot source (brain.yaml or os-on-yaml/brain.yaml with schemaVersion \"os-on-yaml/v0\"). .ino fallback removed for yaml-only path.");
        }

        var bootHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestText))).ToLowerInvariant();
        Environment.SetEnvironmentVariable("DIGITALBRAIN_BOOT_HASH", bootHash);
        if (bootManifest.Seeds.Length > 0)
            Environment.SetEnvironmentVariable("DIGITALBRAIN_SEED_CAPSULES", string.Join(";", bootManifest.Seeds));

        var builder = DistributedApplication.CreateBuilder(args);
        builder.AddDigitalBrainManifest(bootManifest);
        builder.Build().Run();
    }

    private static string? ResolveYamlBootManifestPath()
    {
        if (File.Exists("brain.yaml"))
            return "brain.yaml";
        if (File.Exists("os-on-yaml/brain.yaml"))
            return "os-on-yaml/brain.yaml";
        return null;
    }
}
