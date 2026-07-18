using DigitalBrain.Runtime.Aspire;
using System.Diagnostics;

namespace DigitalBrain.SDK.Microsoft.Aspire;

// Boot-face native Aspire connector. Spawns the DigitalBrain substrate and full Orchestrator.
public sealed class AspireBootConnector : IAspireBootConnector
{
    const string SubstrateName = "digitalbrain";

    readonly string? _kernelProjectPathOverride;
    Process? _appHostProcess;

    // Kernel-path resolution is deferred to SpawnClusterAsync (its only use)
    // so constructing the connector has no side effects: the compile-error
    // and red-scenario boot paths build it but never spawn, and must not
    // crash on a missing path outside BootHost's fault boundary (design §7).
    public AspireBootConnector(string? kernelProjectPath = null)
        => _kernelProjectPathOverride = kernelProjectPath;

    public async Task<string> SpawnClusterAsync(string profile, CancellationToken ct)
    {
        if (_appHostProcess is not null)
            return AspireConnectorStatus.Ok;

        var appHostProject = LocateAppHostProject();
        Console.WriteLine($"[AspireBootConnector] Spawning full Aspire AppHost at: {appHostProject}...");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{appHostProject}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        };

        psi.Environment["DIGITALBRAIN_APPHOST_PROFILE"] = "Product";
        foreach (System.Collections.DictionaryEntry ev in Environment.GetEnvironmentVariables())
        {
            if (ev.Key is string k && ev.Value is string v && !psi.Environment.ContainsKey(k))
            {
                psi.Environment[k] = v;
            }
        }

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch Aspire AppHost process.");

        _appHostProcess = process;
        await Task.Delay(5000, ct);

        return AspireConnectorStatus.Ok;
    }

    // Boot installs only the substrate; wiring the digitalbrain domain's own
    // neurons is a separate spec (E-SDK / Marketplace install, design §1,
    // D-B). This validates the cluster exists and acknowledges the handoff so
    // Genesis can emit !installed — it does not itself install a domain yet.
    public Task<string> InstallDomainAsync(string domain, CancellationToken ct)
        => _appHostProcess is null
            ? throw new InvalidOperationException(
                "InstallDomainAsync called before SpawnClusterAsync — the cluster does not exist yet.")
            : Task.FromResult(AspireConnectorStatus.Ok);

    public async Task WaitForShutdownAsync(CancellationToken ct)
    {
        var process = _appHostProcess ?? throw new InvalidOperationException(
            "WaitForShutdownAsync called before the cluster was spawned.");

        await process.WaitForExitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_appHostProcess is not null)
        {
            try
            {
                if (!_appHostProcess.HasExited)
                {
                    Console.WriteLine("[AspireBootConnector] Tearing down Aspire AppHost process tree...");
                    _appHostProcess.Kill(entireProcessTree: true);
                }
            }
            catch {}
            _appHostProcess.Dispose();
            _appHostProcess = null;
        }
        await Task.CompletedTask;
    }

    public Task<string> RestartResourceAsync(string resource, CancellationToken ct)
        => RunAspireCommandAsync("restart", resource, ct);

    public Task<string> StartResourceAsync(string resource, CancellationToken ct)
        => RunAspireCommandAsync("start", resource, ct);

    public Task<string> StopResourceAsync(string resource, CancellationToken ct)
        => RunAspireCommandAsync("stop", resource, ct);

    async Task<string> RunAspireCommandAsync(string command, string resource, CancellationToken ct)
    {
        var appHost = LocateAppHostProject();
        var psi = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = $"resource {resource} {command} --apphost \"{appHost}\" --non-interactive",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return "failed-to-start";

            await process.WaitForExitAsync(ct);
            if (process.ExitCode == 0)
                return AspireConnectorStatus.Ok;

            var error = await process.StandardError.ReadToEndAsync(ct);
            return $"error: {error.Trim()}";
        }
        catch (Exception ex)
        {
            return $"exception: {ex.Message}";
        }
    }

    static string LocateAppHostProject()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !(File.Exists(Path.Combine(dir, "DigitalBrain.slnx")) || File.Exists(Path.Combine(dir, "DigitalBrain.slnx"))))
            dir = Path.GetDirectoryName(dir);
        if (dir is null)
        {
            dir = Environment.CurrentDirectory;
            while (dir is not null && !(File.Exists(Path.Combine(dir, "DigitalBrain.slnx")) || File.Exists(Path.Combine(dir, "DigitalBrain.slnx"))))
                dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate the repo root (DigitalBrain.slnx or DigitalBrain.slnx) to resolve the AppHost project path.");

        var appHost = Path.Combine(
            dir, "kernel", "DigitalBrain.AppHost", "DigitalBrain.AppHost.csproj");
        if (!File.Exists(appHost))
            throw new InvalidOperationException(
                $"AppHost project not found at expected path '{appHost}'.");
        return appHost;
    }

    static string LocateKernelProject()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !(File.Exists(Path.Combine(dir, "DigitalBrain.slnx")) || File.Exists(Path.Combine(dir, "DigitalBrain.slnx"))))
            dir = Path.GetDirectoryName(dir);
        if (dir is null)
        {
            dir = Environment.CurrentDirectory;
            while (dir is not null && !(File.Exists(Path.Combine(dir, "DigitalBrain.slnx")) || File.Exists(Path.Combine(dir, "DigitalBrain.slnx"))))
                dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate the repo root (DigitalBrain.slnx or DigitalBrain.slnx) to resolve the Kernel project path.");

        var kernel = Path.Combine(
            dir, "kernel", "DigitalBrain.Kernel", "DigitalBrain.Kernel.csproj");
        if (!File.Exists(kernel))
            throw new InvalidOperationException(
                $"Kernel project not found at expected path '{kernel}'.");
        return kernel;
    }
}
