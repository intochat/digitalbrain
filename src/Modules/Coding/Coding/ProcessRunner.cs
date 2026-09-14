using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DigitalBrain.Coding;

public sealed class ProcessRunner : IProcessRunner
{
    private const int MaximumOutputCharacters = 1024 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // MSBuild node reuse and the terminal logger would keep processes and colour codes around.
        start.Environment["MSBUILDNODEREUSE"] = "0";
        start.Environment["MSBUILDTERMINALLOGGER"] = "off";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var clock = Stopwatch.StartNew();
        using var process = new Process { StartInfo = start };
        process.Start();
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), clock.Elapsed, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return new ProcessResult(-1, string.Empty, string.Empty, clock.Elapsed, TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length < MaximumOutputCharacters)
            {
                text.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - text.Length));
            }
        }

        return text.ToString();
    }
}
