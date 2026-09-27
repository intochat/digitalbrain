using System.Collections.Concurrent;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.Tests;

// Answers the docker CLI calls the runner makes: run starts a container, rm removes it, inspect reports it.
internal sealed class FakeDocker(Func<IReadOnlyList<string>, ProcessResult?>? respond = null) : IProcessRunner
{
    private readonly ConcurrentDictionary<string, string> _running = new(StringComparer.Ordinal);

    public ConcurrentQueue<IReadOnlyList<string>> Calls { get; } = new();

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Enqueue(arguments);
        if (respond?.Invoke(arguments) is { } forced) { return Task.FromResult(forced); }
        var container = arguments[^1];
        return Task.FromResult(arguments[0] switch
        {
            "run" => Succeeded(_running[arguments[3]] = "running|0|2026-09-27T10:00:00Z"),
            "rm" => _running.TryRemove(container, out _) ? Succeeded("") : Failed("Error response from daemon: No such container: " + container),
            "inspect" => _running.TryGetValue(container, out var state) ? Succeeded(state) : Failed("Error: No such object: " + container),
            _ => Succeeded(""),
        });
    }

    private static ProcessResult Succeeded(string output) => new(0, output, "", TimeSpan.Zero, false);

    private static ProcessResult Failed(string error) => new(1, "", error, TimeSpan.Zero, false);
}
