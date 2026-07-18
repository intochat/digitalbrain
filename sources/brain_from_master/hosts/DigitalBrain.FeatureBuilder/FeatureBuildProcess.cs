using System.ComponentModel;
using System.Diagnostics;
using System.Text;
namespace DigitalBrain.FeatureBuilder;

internal sealed record FeatureBuildProcessResult(int ExitCode, string Failure)
{
    internal bool Succeeded => ExitCode == 0;
}

internal sealed class FeatureBuildProcess(TimeProvider timeProvider)
{
    private const int MaximumCapturedCharacters = 16_384;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    internal async Task RunAsync(
        string workspace,
        DateTimeOffset deadline,
        TimeSpan stageLimit,
        FeatureBuildFailure failure,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await RunCoreAsync(workspace, deadline, stageLimit, failure, cancellationToken, arguments);
        if (!result.Succeeded)
        {
            throw new FeatureBuildException(failure, result.Failure);
        }
    }
    internal Task<FeatureBuildProcessResult> RunForEvidenceAsync(
        string workspace,
        DateTimeOffset deadline,
        TimeSpan stageLimit,
        CancellationToken cancellationToken,
        params string[] arguments) =>
        RunCoreAsync(workspace, deadline, stageLimit, FeatureBuildFailure.ScenarioFailed, cancellationToken, arguments);
    private async Task<FeatureBuildProcessResult> RunCoreAsync(
        string workspace,
        DateTimeOffset deadline,
        TimeSpan stageLimit,
        FeatureBuildFailure failure,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments)
    {
        var remaining = deadline - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw FeatureBuildDeadline.Expired();
        }
        var timeout = remaining < stageLimit ? remaining : stageLimit;
        using var stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCancellation.CancelAfter(timeout);
        using var process = new Process { StartInfo = CreateStartInfo(workspace, arguments) };
        if (!process.Start())
        {
            throw new FeatureBuildException(failure, "The .NET build process could not start.");
        }
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(stageCancellation.Token);
        var outputTask = ReadBoundedAsync(process.StandardOutput, outputCancellation.Token);
        var errorTask = ReadBoundedAsync(process.StandardError, outputCancellation.Token);
        try
        {
            await process.WaitForExitAsync(stageCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outputCancellation.Cancel();
            Kill(process);
            throw FeatureBuildDeadline.Expired();
        }
        catch
        {
            outputCancellation.Cancel();
            Kill(process);
            throw;
        }
        var output = await outputTask;
        var error = await errorTask;
        return new FeatureBuildProcessResult(
            process.ExitCode,
            process.ExitCode == 0 ? string.Empty : BoundedFailure(arguments[0], process.ExitCode, output, error));
    }
    private static ProcessStartInfo CreateStartInfo(string workspace, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotNetHost(),
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        PopulateEnvironment(startInfo, workspace);
        return startInfo;
    }
    private static void PopulateEnvironment(ProcessStartInfo startInfo, string workspace)
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        var windir = Environment.GetEnvironmentVariable("WINDIR");
        startInfo.Environment.Clear();
        Add(startInfo, "SystemRoot", systemRoot);
        Add(startInfo, "WINDIR", windir);
        var home = Directory.CreateDirectory(Path.Combine(workspace, ".home")).FullName;
        var temporary = Directory.CreateDirectory(Path.Combine(workspace, ".temp")).FullName;
        var applicationData = Directory.CreateDirectory(Path.Combine(home, "AppData", "Roaming")).FullName;
        var localApplicationData = Directory.CreateDirectory(Path.Combine(home, "AppData", "Local")).FullName;
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["USERPROFILE"] = home;
        startInfo.Environment["APPDATA"] = applicationData;
        startInfo.Environment["LOCALAPPDATA"] = localApplicationData;
        startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
        startInfo.Environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
        startInfo.Environment["TEMP"] = temporary;
        startInfo.Environment["TMP"] = temporary;
        startInfo.Environment["DOTNET_CLI_HOME"] = home;
        if (OperatingSystem.IsWindows())
        {
            PopulateWindowsEnvironment(startInfo, home, systemRoot);
        }
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";
        startInfo.Environment["NUGET_XMLDOC_MODE"] = "skip";
    }
    private static void PopulateWindowsEnvironment(ProcessStartInfo startInfo, string home, string? systemRoot)
    {
        var homeRoot = Path.GetPathRoot(home)!;
        startInfo.Environment["HOMEDRIVE"] = homeRoot.TrimEnd(Path.DirectorySeparatorChar);
        startInfo.Environment["HOMEPATH"] = home[homeRoot.Length..].Insert(0, "\\");
        startInfo.Environment["ProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        startInfo.Environment["ProgramFiles(x86)"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        startInfo.Environment["ProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        startInfo.Environment["ALLUSERSPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        startInfo.Environment["ComSpec"] = Path.Combine(systemRoot ?? "C:\\Windows", "System32", "cmd.exe");
    }
    private static void Add(ProcessStartInfo startInfo, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[name] = value;
        }
    }
    private static string ResolveDotNetHost()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var rooted = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(rooted))
            {
                return rooted;
            }
        }
        if (OperatingSystem.IsWindows())
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(installed))
            {
                return installed;
            }
        }
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, "The .NET SDK host could not be resolved.");
    }
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder(MaximumCapturedCharacters);
        var buffer = new char[4_096];
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0)
                {
                    return captured.ToString();
                }
                var remaining = MaximumCapturedCharacters - captured.Length;
                if (remaining > 0)
                {
                    captured.Append(buffer, 0, Math.Min(count, remaining));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return captured.ToString();
        }
        catch (Exception exception) when (
            cancellationToken.IsCancellationRequested && exception is IOException or ObjectDisposedException)
        {
            return captured.ToString();
        }
    }
    private static string BoundedFailure(string stage, int exitCode, string output, string error)
    {
        var detail = string.Join(Environment.NewLine, new[] { output, error }.Where(static value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        return $"dotnet {stage} exited with code {exitCode}.{Environment.NewLine}{detail}";
    }
}
