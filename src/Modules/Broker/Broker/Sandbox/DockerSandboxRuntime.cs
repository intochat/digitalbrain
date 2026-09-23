using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DigitalBrain.Broker.Sandbox;

// The Docker adapter for ISandboxRuntime. It runs one container per call with `--network none`,
// CPU/memory/process limits and a read-only root filesystem. It is the only place the platform
// shells out for process apps; a host without Docker reports unavailable and denies the call.
public sealed class DockerSandboxRuntime : ISandboxRuntime
{
    private bool? available;

    public bool IsAvailable => available ??= ProbeDocker();

    public async ValueTask<SandboxRunResult> RunAsync(
        SandboxRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            return new SandboxRunResult { Succeeded = false, Failure = "Docker is not available on this host." };
        }

        var spec = request.Spec;
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildRunArguments(spec, request))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new SandboxRunResult { Succeeded = false, Failure = "Docker did not start." };
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(spec.Limits.Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new SandboxRunResult { Succeeded = false, Failure = "The sandbox run exceeded its time limit." };
        }

        var output = (await stdout.ConfigureAwait(false)).Trim();
        var error = (await stderr.ConfigureAwait(false)).Trim();
        return new SandboxRunResult
        {
            Succeeded = process.ExitCode == 0,
            Output = output,
            ExitCode = process.ExitCode,
            Failure = process.ExitCode == 0 ? null : error,
        };
    }

    public static IReadOnlyList<string> BuildRunArguments(SandboxSpec spec, SandboxRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new List<string> { "run", "--rm" };
        arguments.Add("--network");
        arguments.Add(spec.Network == SandboxNetworkPolicy.Denied ? "none" : "bridge");
        arguments.Add("--cpus");
        arguments.Add(spec.Limits.CpuCount.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--memory");
        arguments.Add(spec.Limits.MemoryBytes.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--pids-limit");
        arguments.Add(spec.Limits.ProcessCount.ToString(CultureInfo.InvariantCulture));
        if (spec.ReadOnlyRootFilesystem)
        {
            arguments.Add("--read-only");
            arguments.Add("--tmpfs");
            arguments.Add("/tmp");
        }

        foreach (var (key, value) in spec.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            arguments.Add("-e");
            arguments.Add($"{key}={value}");
        }
        arguments.Add("-e");
        arguments.Add($"INTOCHAT_INPUT={request.Input ?? string.Empty}");
        arguments.Add(spec.Image);
        arguments.AddRange(spec.Command);
        return arguments;
    }

    private static bool ProbeDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker")
            {
                Arguments = "version --format {{.Server.Version}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }
            if (!process.WaitForExit(5000))
            {
                TryKill(process);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
        }
    }
}