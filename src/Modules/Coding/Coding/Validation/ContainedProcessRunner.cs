using System.Diagnostics;
using System.Text;

namespace DigitalBrain.Coding;

public sealed class ContainedProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var clock = Stopwatch.StartNew();
        using var child = WindowsContainedProcess.Start(fileName, arguments, workingDirectory);
        var output = Capture(child.Output, deadline.Token);
        var error = Capture(child.Error, deadline.Token);
        var timedOut = false;
        try { await child.Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { timedOut = true; }
        finally
        {
            child.Terminate();
            await child.Process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
        }
        var stdout = await output.ConfigureAwait(false);
        var stderr = await error.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(child.Process.ExitCode, stdout, stderr, clock.Elapsed, timedOut);
    }

    private static async Task<string> Capture(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            { if (result.Length < 65536) { result.Append(buffer, 0, Math.Min(count, 65536 - result.Length)); } }
        }
        catch (OperationCanceledException) { }
        return result.ToString();
    }
}
