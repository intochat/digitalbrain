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
        var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), clock.Elapsed, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return new ProcessResult(-1, await DrainAsync(stdout).ConfigureAwait(false), await DrainAsync(stderr).ConfigureAwait(false), clock.Elapsed, TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await DrainAsync(stdout).ConfigureAwait(false);
            await DrainAsync(stderr).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                Kill(process);
            }
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
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (text.Length < MaximumOutputCharacters)
                {
                    text.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - text.Length));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The deadline can cancel a read that is already in flight; whatever was captured in earlier
            // iterations is still worth handing back as partial output rather than losing it to the fault.
        }

        return text.ToString();
    }

#pragma warning disable CA1031 // a read left blocked on the pipe of a killed process can fault with anything; the outcome is already decided and the read must still be observed before Dispose closes the handles
    private static async Task<string> DrainAsync(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
#pragma warning restore CA1031
}
