using System.Collections.Concurrent;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.Tests;

internal sealed class RecordingProcessRunner(Func<IReadOnlyList<string>, ProcessResult>? respond = null) : IProcessRunner
{
    public ConcurrentQueue<IReadOnlyList<string>> Calls { get; } = new();

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Enqueue(arguments);
        return Task.FromResult(respond?.Invoke(arguments) ?? new ProcessResult(0, "", "", TimeSpan.Zero, false));
    }
}
