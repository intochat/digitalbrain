using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Hosting.Microsoft.Flutter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Runtime;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DigitalBrain.Hosting.DigitalBrain;

// Launcher for AspireHosted / WorldId / fork paths (process spawn of AppHost with env isolation for separate clusters).
// Supports DigitalBrain domains (from AddDigitalBrainDomain in AppHost): worldId comes from DigitalBrainDomainResource.WorldId, DIGITALBRAIN_* set per-domain for orleans cluster isolation + kernel experiences scoped to domain.
// Root domain is meta (launches others via IAspire.StartNew / brain.StartWorldAsync).
// Lives under the DigitalBrain product boundary in Sdk (light, only needs Orleans client).
// Simulation default stays fast/in-mem. Best-effort (never crashes REPL or grain callers).
public static class DigitalBrainLauncher
{
    public static async Task<IDigitalBrainClient> LaunchAsync(DigitalBrainStartOptions options, CancellationToken cancellationToken = default)
    {
        if (options.Mode == DigitalBrainLaunchMode.Simulation && string.IsNullOrWhiteSpace(options.WorldId))
        {
            return await IDigitalBrain.StartNew(cancellationToken);
        }

        var world = string.IsNullOrWhiteSpace(options.WorldId) ? "primary" : options.WorldId;
        if (world.Contains("AppHost", StringComparison.OrdinalIgnoreCase) || world == "DigitalBrain.AppHost")
            world = "primary";
        var clusterId = $"digitalbrain-{Sanitize(world)}";
        var serviceId = "digitalbrain";

        int siloPort = FindFreeTcpPort(11211);
        int gatewayPort = FindFreeTcpPort(30200);

        if (options.Mode == DigitalBrainLaunchMode.ConnectExisting)
        {
            try
            {
                var gatewayEndpoint = ResolveGatewayEndpoint(options.GatewayAddress, gatewayPort);
                using (var tcpProbe = new TcpClient())
                {
                    var connectTask = tcpProbe.ConnectAsync(gatewayEndpoint.Address, gatewayEndpoint.Port);
                    if (await Task.WhenAny(connectTask, Task.Delay(1500, cancellationToken)) != connectTask || !tcpProbe.Connected)
                    {
                        Console.WriteLine($"launcher: connect-existing for world '{world}' at {gatewayEndpoint} (no spawn); no listener, returning marker.");
                        return new DefaultDigitalBrainClient();
                    }
                }

                var clientHost = await CreateConnectedOrleansClientHostAsync(clusterId, serviceId, gatewayEndpoint, cancellationToken);
                var realCc = clientHost.Services.GetRequiredService<IClusterClient>();
                var worldInfo = new WorldConnectionInfo(world, clusterId, serviceId, gatewayEndpoint.ToString(), DashboardUrl: null);
                RequestContext.Set("db.world", world);
                Console.WriteLine($"launcher: connect-existing for world '{world}' at {gatewayEndpoint} (no spawn); cluster client connected for GetGrain<IDigitalBrain>/IMarketplace/IAspire on existing real world.");
                return new RealDigitalBrainClient(realCc, clientHost, currentWorld: worldInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"launcher: connect-existing for world '{world}' (gateway '{options.GatewayAddress ?? $"127.0.0.1:{gatewayPort}"}') failed (best-effort; {ex.Message}). Returning marker (no spawn).");
                return new DefaultDigitalBrainClient();
            }
        }

        var currentWorkingDir = Directory.GetCurrentDirectory();
        var projectArg = "src/DigitalBrain.AppHost";  // consistent src/ layout; no legacy dir discovery or .. fallback (deleted magic path hack per review)
        // Domain wiring (from WithKernelProject/WithKernel on DigitalBrainDomainResource): per-domain kernel uses env-isolated via DIGITALBRAIN_WORLD_ID; AssociatedKernelResourceName used by IAspire for targeted restart/management from meta brain StartWorld.
        // Future: pass domain-specific kernel path for true per-domain projects.

        var appHostStartInfo = new ProcessStartInfo("dotnet", $"run --project \"{projectArg}\" --no-launch-profile")
        {
            WorkingDirectory = currentWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        appHostStartInfo.Environment["DIGITALBRAIN_WORLD_ID"] = world;
        appHostStartInfo.Environment["DIGITALBRAIN_CLUSTER_ID"] = clusterId;
        appHostStartInfo.Environment["DIGITALBRAIN_SERVICE_ID"] = serviceId;
        appHostStartInfo.Environment["DIGITALBRAIN_SILO_PORT"] = siloPort.ToString();
        appHostStartInfo.Environment["DIGITALBRAIN_GATEWAY_PORT"] = gatewayPort.ToString();
        appHostStartInfo.Environment["DIGITALBRAIN_ROOT_GATEWAY"] = Environment.GetEnvironmentVariable("DIGITALBRAIN_ROOT_GATEWAY") ?? "127.0.0.1:30000";
        var setup = new DefaultSetup();
        var gemmaEp = Environment.GetEnvironmentVariable("GEMMA_ENDPOINT") ?? "http://localhost:11434";
        var nemotronEp = Environment.GetEnvironmentVariable("NEMOTRON_ENDPOINT") ?? "http://localhost:11434";
        appHostStartInfo.Environment["ConnectionStrings__gemma"] = gemmaEp;
        appHostStartInfo.Environment["ConnectionStrings__nemotron"] = nemotronEp;
        // Unconditional from Setup so child worlds get the DefaultSetup gemma3:1b default without requiring env from the caller.
        appHostStartInfo.Environment["GEMMA_MODEL"] = setup.GemmaModel;
        appHostStartInfo.Environment["NEMOTRON_MODEL"] = "nemotron-3-nano";

        // Quiet child apphost + domain kernels so IAspire-launched worlds (drained by DrainOutput) emit no console trash.
        // Full telemetry still ships via OTel to dashboard / aspire otel / aspire logs.
        appHostStartInfo.Environment["Logging__LogLevel__Default"] = "Warning";
        appHostStartInfo.Environment["Logging__LogLevel__Orleans"] = "Warning";
        appHostStartInfo.Environment["Logging__LogLevel__Microsoft.Orleans"] = "Warning";
        appHostStartInfo.Environment["Logging__LogLevel__Microsoft"] = "Warning";
        appHostStartInfo.Environment["Logging__LogLevel__OpenTelemetry"] = "Warning";

        string? capturedDashboardUrl = null;
        Process? appHostProcess = null;
        try
        {
            appHostProcess = Process.Start(appHostStartInfo);
            if (appHostProcess is not null)
            {
                _ = Task.Run(() => DrainOutput(appHostProcess, world, line =>
                {
                    if (capturedDashboardUrl is null && TryExtractDashboardUrl(line, out var u))
                        capturedDashboardUrl = u;
                }), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"launcher: spawn failed for world '{world}' (best-effort; {ex.Message})");
            return new DefaultDigitalBrainClient();
        }

        _ = Task.Run(async () =>
        {
            try { await WaitForGatewayReady(gatewayPort, TimeSpan.FromSeconds(30)); }
            catch { }
        });

        RealDigitalBrainClient? verifiedClient = null;
        var connectDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < connectDeadline)
        {
            try
            {
                using (var tcpProbe = new TcpClient())
                {
                    var connectTask = tcpProbe.ConnectAsync(IPAddress.Loopback, gatewayPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(150, cancellationToken)) != connectTask || !tcpProbe.Connected) { await Task.Delay(350, cancellationToken); continue; }
                }

                var clientHost = await CreateConnectedOrleansClientHostAsync(clusterId, serviceId, new IPEndPoint(IPAddress.Loopback, gatewayPort), cancellationToken);
                var realCc = clientHost.Services.GetRequiredService<IClusterClient>();

                var remoteBrain = realCc.GetGrain<IDigitalBrain>(Brain.WellKnownKey);
                var remoteSubs = await remoteBrain.ListSubscribersAsync("InstallBundle", cancellationToken);

                var worldInfo = new WorldConnectionInfo(world, clusterId, serviceId, $"127.0.0.1:{gatewayPort}", DashboardUrl: capturedDashboardUrl);
                RequestContext.Set("db.world", world);
                verifiedClient = new RealDigitalBrainClient(realCc, clientHost, capturedDashboardUrl, appHostProcess, worldInfo);
                Console.WriteLine($"launcher: real AspireHosted world '{world}' launched (cluster={clusterId}, silo={siloPort}, gw={gatewayPort}); client connected and verified on real brain (InstallBundle subs on remote: {remoteSubs.Count}). DashboardUrl={(capturedDashboardUrl ?? "n/a")}");
                break;
            }
            catch
            {
                await Task.Delay(400, cancellationToken);
            }
        }

        if (verifiedClient != null)
        {
            return verifiedClient;
        }

        // Could not connect to the world we spawned — kill the child so it does not linger as an orphan AppHost.
        KillProcessTree(appHostProcess, world);
        Console.WriteLine($"launcher: client connect to launched world '{world}' failed (best-effort after 60s; no remote brain activation). Returning marker.");
        return new DefaultDigitalBrainClient();
    }

    internal static void KillProcessTree(Process? process, string world)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"launcher: failed to kill spawned AppHost for world '{world}' (best-effort; {ex.Message}).");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string Sanitize(string s) => new string(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

    private static int FindFreeTcpPort(int startHint)
    {
        for (int p = startHint; p < startHint + 500; p++)
        {
            try
            {
                using var portListener = new TcpListener(IPAddress.Loopback, p);
                portListener.Start();
                portListener.Stop();
                return p;
            }
            catch { }
        }
        return startHint;
    }

    private static async Task WaitForGatewayReady(int gwPort, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                using var tcpProbe = new TcpClient();
                var connectTask = tcpProbe.ConnectAsync(IPAddress.Loopback, gwPort);
                if (await Task.WhenAny(connectTask, Task.Delay(80)) == connectTask && tcpProbe.Connected)
                    return;
            }
            catch { }
            await Task.Delay(120);
        }
    }

    private static async Task DrainOutput(Process process, string world, Action<string>? onLine = null)
    {
        try
        {
            if (process.StandardOutput is not null)
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
                {
                    onLine?.Invoke(line);
                    if (line.Contains("Application started", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Silo started", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Distributed application", StringComparison.OrdinalIgnoreCase))
                    {
                        // readiness signal observed
                    }
                }
            }
        }
        catch { }
    }

    public static bool TryExtractDashboardUrl(string line, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        const string marker = "Login to the dashboard at ";
        var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = line[(idx + marker.Length)..].Trim();
            var spaceIdx = rest.IndexOf(' ');
            if (spaceIdx > 0) rest = rest[..spaceIdx];
            rest = StripTrailingPunct(rest);
            if (Uri.TryCreate(rest, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                url = uri.ToString();
                return true;
            }
        }

        // Prefer any full /login?t=... (token) url in the line so copy of whole url works directly in browser (handles variations and non-marker prints).
        var tokenIdx = line.IndexOf("/login?t=", StringComparison.OrdinalIgnoreCase);
        if (tokenIdx >= 0)
        {
            var start = line.LastIndexOf("http", tokenIdx, StringComparison.OrdinalIgnoreCase);
            if (start < 0) start = line.LastIndexOf("https", tokenIdx, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var candidate = line[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(candidate))
                {
                    candidate = StripTrailingPunct(candidate);
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                    {
                        url = u.ToString();
                        return true;
                    }
                }
            }
        }

        // Fallback: any localhost dashboard-like url (some outputs use "Dashboard: ..." etc). Still better than nothing for current cluster.
        if (line.Contains("http://localhost", StringComparison.OrdinalIgnoreCase) || line.Contains("https://localhost", StringComparison.OrdinalIgnoreCase))
        {
            var start = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var candidate = line[start..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(candidate))
                {
                    candidate = StripTrailingPunct(candidate);
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var u)) { url = u.ToString(); return true; }
                }
            }
        }
        return false;
    }

    private static string StripTrailingPunct(string s)
    {
        while (s.Length > 0 && (s[^1] == '.' || s[^1] == ',' || s[^1] == ')' || s[^1] == ']' || s[^1] == ';'))
            s = s[..^1];
        return s;
    }

    private static async Task<IHost> CreateConnectedOrleansClientHostAsync(string clusterId, string serviceId, IPEndPoint gatewayEndpoint, CancellationToken cancellationToken)
    {
        var clientBuilder = Host.CreateApplicationBuilder();
        clientBuilder.UseOrleansClient(client =>
        {
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = clusterId;
                o.ServiceId = serviceId;
            });
            client.UseStaticClustering(gatewayEndpoint);
        });
        var clientHost = clientBuilder.Build();
        await clientHost.StartAsync(cancellationToken);
        return clientHost;
    }

    private static IPEndPoint ResolveGatewayEndpoint(string? gatewayAddress, int fallbackPort)
    {
        if (string.IsNullOrWhiteSpace(gatewayAddress))
            return new IPEndPoint(IPAddress.Loopback, fallbackPort);

        var separator = gatewayAddress.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(gatewayAddress[(separator + 1)..], out var port))
            throw new ArgumentException($"Gateway address '{gatewayAddress}' must be host:port.");

        var host = gatewayAddress[..separator];
        var address = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        return new IPEndPoint(address, port);
    }

    public static async Task LaunchInoSessionTerminalAsync(string sessionId, string? worldId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var w = string.IsNullOrWhiteSpace(worldId) ? "primary" : worldId;
        var currentWorkingDir = Directory.GetCurrentDirectory();
        var projectArg = "src/DigitalBrain.Clients.Console";
        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{projectArg}\" --no-launch-profile")
        {
            WorkingDirectory = currentWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false // dedicated visible console terminal
        };
        startInfo.Environment["DIGITALBRAIN_INO_SESSION"] = "1";
        startInfo.Environment["DIGITALBRAIN_WORLD_ID"] = w;
        var cluster = $"digitalbrain-{Sanitize(w)}";
        startInfo.Environment["DIGITALBRAIN_CLUSTER_ID"] = cluster;
        startInfo.Environment["DIGITALBRAIN_SERVICE_ID"] = "digitalbrain";
        startInfo.Environment["DIGITALBRAIN_GRPC_ENDPOINT"] = Environment.GetEnvironmentVariable("DIGITALBRAIN_GRPC_ENDPOINT") ?? "http://localhost:8080";
        var rootGw = Environment.GetEnvironmentVariable("DIGITALBRAIN_ROOT_GATEWAY");
        if (!string.IsNullOrWhiteSpace(rootGw))
            startInfo.Environment["DIGITALBRAIN_ROOT_GATEWAY"] = rootGw;
        try
        {
            Process.Start(startInfo);
            Console.WriteLine($"launcher: spawned dedicated InoSession terminal for '{sessionId}' (world={w}, cluster={cluster})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"launcher: failed to spawn ino terminal for {sessionId} (best-effort; {ex.Message})");
        }
    }

    public static async Task EnsureForDomainAsync(IGrainFactory gf, string worldId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worldId)) return;
        var cluster = $"digitalbrain-{Sanitize(worldId)}";
        await gf.GetGrain<IDigitalBrain>(worldId).InstallBundleAsync("kernel-tasks", cancellationToken);
        // All brains (root + worlds) default connect to marketplace (search + install via public contract).
        // "global" key = private silo impl (MarketplaceNeuron + redis in root kernel); public contract = IMarketplace (Core) + .ino + MCP tools + L1 .brain.
        await gf.GetGrain<INeuron>("global").EnsureActiveAsync();
    }

    public static async Task EnsureDomainExperiencesAsync(IGrainFactory gf)
    {
        string[] coreDomainIds = ["awesome-se-team", "kernel-tasks", "creator", "marketplace", "packager", "transcription"];
        foreach (var id in coreDomainIds)
        {
            var key = id is "creator" or "marketplace" or "packager" ? "global" : "default";
            try
            {
                var neuron = gf.GetGrain<INeuron>(key);
                await neuron.EnsureActiveAsync();
            }
            catch
            {
                // Best-effort pre-activation for launcher convenience. Normal InstallBundle + dispatch activates the right implementation.
            }
        }

        // Auto-start the Flutter renderer (via IFlutter neuron) for aspire-hosted / world launches.
        // When under real DistributedApplication the grain drives flutter-web or flutter-windows via ResourceCommandService (start/restart) based on target.
        // In standalone (start.cs etc) falls back to best-effort flutter process spawn. Brain-owned, journaled, same as IAspire for kernels.
        try
        {
            var f = gf.GetGrain<IFlutter>(Brain.WellKnownKey);
            _ = f.StartFlutterClientAsync();
        }
        catch
        {
            // Best effort; the StartFlutterClient synapse + FlutterClientStarted will carry the outcome.
        }
    }

    public static async Task ActivateExperiencesFor(IGrainFactory gf, BundleId bundleId)
    {
        var id = bundleId.Value.ToLowerInvariant();
        var key = id == "creator" ? "global" : id;
        try
        {
            var neuron = gf.GetGrain<INeuron>(key);
            await neuron.EnsureActiveAsync();
        }
        catch
        {
            // Best-effort; synthetic/test bundle ids (e.g. ino:domain-sim-gate-e2e used in VerifyDurableJournalReplay) or unknown domains
            // produce prefixes with no grain impl. Real installs go through bundle wiring + dispatch manifest (or interface scan fallback).
        }
    }

    // Awesome marketplace seeding (awesome-se-team listing) — sole owner is Aspire AddStartupTask registration (root kernel).
    // Interlocked guard ensures exactly once per process (same semantics as prior). Tests do not call this; they pack/publish explicitly per-scenario for isolation.
    private static int _awesomeSeeded;
    public static async Task SeedAwesomeMarketplaceOnceAsync(IGrainFactory gf, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _awesomeSeeded, 1) == 1) return;
        try
        {
            var marketplace = gf.GetGrain<IMarketplace>(Brain.WellKnownKey);
            var listings = await marketplace.ListAsync(cancellationToken);
            if (listings.Any(l => string.Equals(l.Manifest.Id, "awesome-se-team", StringComparison.OrdinalIgnoreCase))) return;

            var packed = await gf.GetGrain<IPackager>(Brain.WellKnownKey)
                .PackAsync("awesome-se-team", "Software engineering team: install, then ask ino to analyze a C# project at a kernel-local path", cancellationToken: cancellationToken);
            await marketplace.PublishLocalAsync("awesome-se-team", packed.PackagePath, cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref _awesomeSeeded, 0);
        }
    }

    static string PrefixFor(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "awesome-se-team" or "awesome" => "AwesomeSoftwareEngineeringTeamNeuron",
            "kernel-tasks" or "tasks" or "kerneltasks" => "KernelTaskSupervisor",
            "creator" => "CreatorNeuron",
            "llm" or "agent" or "llm-agent" => "LlmAgentNeuron", // F T2: impl now in DigitalBrain.Ino assembly (on SDK); string name map + [GrainType] unchanged for activation/launcher compat.
            "weather" or "weather-watcher" or "weather-watcher-demo" => "WeatherWatcherNeuron",
            "memory" => "MemoryNeuron", // F T2: impl now in DigitalBrain.Ino (extraction complete; name map preserved).
            "hex1b-guide" or "guide" or "hexguide" => "HexGuideNeuron",
            "marketplace" or "market" => "MarketplaceNeuron",
            "packager" or "pack" => "PackagerNeuron",
            "flutter" or "flutter-client" or "ui" => "Flutter",
            // T2 connectors: name map updated for self-exp class names in DigitalBrain.Sdk.Experiences (FileSystemConnectorGrain etc). "fs"/"filesystem" now maps to new; GrainType("filesystem") set on impl (vision "filesystem"). google/gmail entries added for completeness (GrainType strings "google-auth"/"gmail-last-senders" unchanged for seeds/activation compat).
            "fs" or "filesystem" => "FileSystemConnectorGrain",
            "google-auth" or "googleauth" or "gauth" => "GoogleAuthConnectorNeuron",
            "gmail-last-senders" or "gmail" => "GmailConnectorNeuron",
            "telegram-bot" or "telegram" or "tg" => "TelegramConnectorNeuron",
            "transcription" or "voice" or "stt" => "TranscriptionNeuron",
            _ => char.ToUpperInvariant(id[0]) + (id.Length > 1 ? id[1..] : "") + "Neuron"
        };
    }
}

// Real client returned by launcher for AspireHosted/WorldId cases (self-explanatory). Exposes ClusterClient so Start(options) yields usable "GetGrain real IDigitalBrain + IAspire".
// Also carries DashboardUrl (captured from the child AppHost/DistributedApplication output).
public sealed class RealDigitalBrainClient : IDigitalBrainClient
{
    private readonly IClusterClient _clusterClient;
    private readonly IHost? _host;
    private readonly string? _dashboardUrl;
    private readonly Process? _appHostProcess;
    private readonly WorldConnectionInfo? _currentWorld;

    public RealDigitalBrainClient(IClusterClient clusterClient, IHost? host = null, string? dashboardUrl = null, Process? appHostProcess = null, WorldConnectionInfo? currentWorld = null)
    {
        _clusterClient = clusterClient;
        _host = host;
        _dashboardUrl = dashboardUrl;
        _appHostProcess = appHostProcess;
        _currentWorld = currentWorld;
    }

    public string? DashboardUrl => _dashboardUrl;
    public IClusterClient? ClusterClient => _clusterClient;
    public WorldConnectionInfo? CurrentWorld => _currentWorld;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            try { await _host.StopAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { Console.WriteLine($"launcher: failed to stop Orleans client host during dispose (best-effort; {ex.Message})."); }
            _host.Dispose();
        }

        // The spawned AppHost is owned by this client — tearing it down here is what prevents orphaned worlds.
        DigitalBrainLauncher.KillProcessTree(_appHostProcess, _dashboardUrl ?? "launched");
    }
}
