using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal sealed partial class DotNetScriptLauncher : IScriptLauncher
{
    private const int SigTerm = 15;

    public IScriptProcess Start(string workDirectory, IReadOnlyDictionary<string, string> environment, Action<string> output)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["run", "--verbosity", "quiet", "app.cs", "-p:ArtifactsPath=" + Path.Combine(workDirectory, "artifacts")])
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft"] = "Warning";
        foreach (var (name, value) in environment) { start.Environment[name] = value; }
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) { output(line.Data); } };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) { output(line.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new DotNetScript(process);
    }

    private sealed class DotNetScript : IScriptProcess
    {
        private readonly Process _process;

        public DotNetScript(Process process)
        {
            _process = process;
            Completion = WaitAsync(process);
        }

        public Task<int> Completion { get; }

        private static async Task<int> WaitAsync(Process process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }

        // `dotnet run` forwards SIGTERM to the app, so the script's host stops and unsubscribes cleanly.
        public async Task StopAsync(TimeSpan grace)
        {
            if (_process.HasExited) { return; }
            if (OperatingSystem.IsWindows()) { _process.Kill(entireProcessTree: true); }
            else { _ = Kill(_process.Id, SigTerm); }
            if (await Task.WhenAny(Completion, Task.Delay(grace)).ConfigureAwait(false) != Completion)
            {
                _process.Kill(entireProcessTree: true);
            }
            await Completion.ConfigureAwait(false);
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int processId, int signal);
}
