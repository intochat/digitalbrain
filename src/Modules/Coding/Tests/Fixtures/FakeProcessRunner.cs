using DigitalBrain.Coding;

namespace DigitalBrain.Tests.Coding;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Calls { get; } = [];

    public void Enqueue(int exitCode, string output, string error = "", bool timedOut = false)
        => _results.Enqueue(new ProcessResult(exitCode, output, error, TimeSpan.FromSeconds(1), timedOut));

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments, workingDirectory));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));
    }
}
