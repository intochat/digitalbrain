using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.Tests;

internal sealed class ScriptedProcessScript
{
    private readonly Lock gate = new();
    private readonly Queue<ProcessResult> builds = new();
    private readonly Queue<ProcessResult> tests = new();
    private readonly List<string> invocations = [];

    public ProcessResult DefaultBuild { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false);

    public ProcessResult DefaultTest { get; set; } = new(0, "total: 1, failed: 0, succeeded: 1, skipped: 0", string.Empty, TimeSpan.Zero, TimedOut: false);

    public bool IsGitRepository { get; set; }

    public string RepositoryRoot { get; set; } = string.Empty;

    public void QueueBuild(ProcessResult result)
    {
        lock (gate)
        {
            builds.Enqueue(result);
        }
    }

    public void QueueTest(ProcessResult result)
    {
        lock (gate)
        {
            tests.Enqueue(result);
        }
    }

    public ProcessResult NextBuild()
    {
        lock (gate)
        {
            return builds.Count > 0 ? builds.Dequeue() : DefaultBuild;
        }
    }

    public ProcessResult NextTest()
    {
        lock (gate)
        {
            return tests.Count > 0 ? tests.Dequeue() : DefaultTest;
        }
    }

    public void Record(string invocation)
    {
        lock (gate)
        {
            invocations.Add(invocation);
        }
    }

    public IReadOnlyList<string> Invocations
    {
        get
        {
            lock (gate)
            {
                return invocations.ToArray();
            }
        }
    }
}

internal sealed class ScriptedProcessRunner(ScriptedProcessScript script) : IProcessRunner
{
    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        script.Record($"{fileName} {string.Join(' ', arguments)}");
        if (fileName == "git")
        {
            return Task.FromResult(arguments.Contains("rev-parse")
                ? script.IsGitRepository
                    ? new ProcessResult(0, script.RepositoryRoot, string.Empty, TimeSpan.Zero, TimedOut: false)
                    : new ProcessResult(128, string.Empty, "not a git repository", TimeSpan.Zero, TimedOut: false)
                : new ProcessResult(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));
        }

        if (arguments.Count > 0 && arguments[0] == "build")
        {
            return Task.FromResult(script.NextBuild());
        }

        if (arguments.Count > 0 && arguments[0] == "test")
        {
            return Task.FromResult(script.NextTest());
        }

        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimeSpan.Zero, TimedOut: false));
    }
}
