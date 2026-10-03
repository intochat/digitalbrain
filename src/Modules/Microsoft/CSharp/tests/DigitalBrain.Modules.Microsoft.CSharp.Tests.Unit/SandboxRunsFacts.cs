using DigitalBrain.Microsoft.CSharp.Sandbox;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class SandboxRunsFacts : IDisposable
{
    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "sandbox-runs-facts", Guid.NewGuid().ToString("N"));
    private readonly FakeLauncher _launcher = new();

    [Fact]
    public async Task ARunGetsItsOwnFolderReferencingTheBrainClientAndItsEnvironment()
    {
        var ct = TestContext.Current.CancellationToken;
        var runs = Runs();

        var started = await runs.StartAsync("run-1", new("Console.WriteLine(1);", new() { ["DigitalBrain__Edge"] = "http://edge.test/" }), ct);

        Assert.Equal(RunStatuses.Running, started.Status);
        var work = Path.Combine(_workRoot, "run-1");
        Assert.Equal("Console.WriteLine(1);", await File.ReadAllTextAsync(Path.Combine(work, "app.cs"), ct));
        Assert.Contains("/brain/client.csproj", await File.ReadAllTextAsync(Path.Combine(work, "Directory.Build.props"), ct), StringComparison.Ordinal);
        var launch = Assert.Single(_launcher.Launches);
        Assert.Equal(work, launch.WorkDirectory);
        Assert.Equal("http://edge.test/", launch.Environment["DigitalBrain__Edge"]);
    }

    [Fact]
    public async Task SeveralScriptsRunSideBySideAndReportTheirOwnExitAndOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var runs = Runs();
        await runs.StartAsync("run-a", new("a", null), ct);
        await runs.StartAsync("run-b", new("b", null), ct);

        _launcher.Launches[0].Process.Output("compiled a");
        _launcher.Launches[0].Process.Exit(3);

        Assert.Equal(new RunStatus("run-a", RunStatuses.Exited, 3, DateTimeOffset.UnixEpoch), runs.Find("run-a"));
        Assert.Equal(RunStatuses.Running, runs.Find("run-b")!.Status);
        Assert.Equal("compiled a", runs.ReadLogs("run-a", 10));
        Assert.Null(runs.Find("missing"));
    }

    [Fact]
    public async Task StoppingEndsOnlyThatRunAndARunningIdentifierCannotStartTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var runs = Runs();
        await runs.StartAsync("run-a", new("a", null), ct);
        await runs.StartAsync("run-b", new("b", null), ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runs.StartAsync("run-a", new("again", null), ct));
        var stopped = await runs.StopAsync("run-a");

        Assert.Equal(RunStatuses.Exited, stopped!.Status);
        Assert.Equal(RunStatuses.Running, runs.Find("run-b")!.Status);
        await runs.StartAsync("run-a", new("again", null), ct);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("../escape")]
    [InlineData("has space")]
    public async Task RejectsIdentifiersThatCouldNotBeAFolderName(string identifier)
        => await Assert.ThrowsAsync<ArgumentException>(() => Runs().StartAsync(identifier, new("x", null), TestContext.Current.CancellationToken));

    public void Dispose()
    {
        if (Directory.Exists(_workRoot)) { Directory.Delete(_workRoot, recursive: true); }
    }

    private SandboxRuns Runs() => new(_launcher,
        Options.Create(new SandboxOptions { WorkRoot = _workRoot, ClientProject = "/brain/client.csproj", LogLines = 100 }),
        new FixedTime());

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeLauncher : IScriptLauncher
    {
        public List<(string WorkDirectory, IReadOnlyDictionary<string, string> Environment, FakeProcess Process)> Launches { get; } = [];

        public IScriptProcess Start(string workDirectory, IReadOnlyDictionary<string, string> environment, Action<string> output)
        {
            var process = new FakeProcess(output);
            Launches.Add((workDirectory, environment, process));
            return process;
        }
    }

    private sealed class FakeProcess(Action<string> output) : IScriptProcess
    {
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> Completion => _exit.Task;

        public void Output(string line) => output(line);

        public void Exit(int code) => _exit.TrySetResult(code);

        public Task StopAsync(TimeSpan grace)
        {
            Exit(143);
            return Task.CompletedTask;
        }
    }
}
