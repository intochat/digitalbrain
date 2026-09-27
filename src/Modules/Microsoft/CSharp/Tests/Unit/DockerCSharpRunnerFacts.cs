using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Microsoft.DotNet;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace DigitalBrain.Tests;

public sealed class DockerCSharpRunnerFacts : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "csharp-runner-facts", Guid.NewGuid().ToString("N"));

    private CSharpOptions Settings => new()
    {
        Root = _root, SourceRoot = @"E:\repo", Gateways = "gwy.tcp://127.0.0.1:30000/0", GatewayRelayHost = "host.docker.internal", ClusterId = "cluster", ServiceId = "service",
    };

    [Fact]
    public async Task StartReplacesTheContainerAndRunsTheFileWithSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        var processes = new RecordingProcessRunner();
        var runner = new DockerCSharpRunner(processes, Create(Settings));

        await runner.StartAsync("workspace/timer", "Console.WriteLine(1);", new Dictionary<string, string> { ["Greeting"] = "a = b c" }, ct);

        var calls = processes.Calls.ToArray();
        var container = DockerCSharpRunner.ContainerName("workspace/timer");
        Assert.Equal(["rm", "--force", container], calls[0]);
        var run = calls[1];
        Assert.Equal(["run", "--detach", "--name", container], run.Take(4));
        Assert.Contains("on-failure", run);
        Assert.Contains(@"E:\repo:/brain:ro", run);
        Assert.Contains("CSharpFile__Settings__Greeting=a = b c", run);
        Assert.Contains("CSharpFile__Id=workspace/timer", run);
        Assert.Contains("Gateways=gwy.tcp://127.0.0.1:30000/0", run);
        Assert.Contains("GatewayRelayHost=host.docker.internal", run);
        Assert.Equal(["mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1", "dotnet", "run", "app.cs", "-p:ArtifactsPath=/work/artifacts"], run.TakeLast(5));
        var work = Path.Combine(_root, container);
        Assert.Equal("Console.WriteLine(1);", await File.ReadAllTextAsync(Path.Combine(work, "app.cs"), ct));
        Assert.Contains(DockerCSharpRunner.ClientProject, await File.ReadAllTextAsync(Path.Combine(work, "Directory.Build.props"), ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoppingAMissingContainerIsNotAnError()
    {
        var ct = TestContext.Current.CancellationToken;
        var processes = new RecordingProcessRunner(_ => new ProcessResult(1, "", "Error response from daemon: No such container: csharp-x", TimeSpan.Zero, false));
        var runner = new DockerCSharpRunner(processes, Create(Settings));

        await runner.StopAsync("never-started", ct);

        Assert.Equal(CSharpFileStatus.Stopped, (await runner.InspectAsync("never-started", ct)).Status);
    }

    [Fact]
    public async Task FailedDockerRunSurfacesTheDaemonError()
    {
        var processes = new RecordingProcessRunner(arguments => arguments[0] == "run"
            ? new ProcessResult(125, "", "Cannot connect to the Docker daemon", TimeSpan.Zero, false)
            : new ProcessResult(0, "", "", TimeSpan.Zero, false));
        var runner = new DockerCSharpRunner(processes, Create(Settings));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync("a", "x", new Dictionary<string, string>(), TestContext.Current.CancellationToken));

        Assert.Contains("Cannot connect to the Docker daemon", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("running|0|2026-09-27T10:00:00.1234567Z", CSharpFileStatus.Running, null)]
    [InlineData("exited|0|2026-09-27T10:00:00Z", CSharpFileStatus.Exited, 0)]
    [InlineData("exited|137|2026-09-27T10:00:00Z", CSharpFileStatus.Exited, 137)]
    [InlineData("restarting|1|2026-09-27T10:00:00Z", CSharpFileStatus.Restarting, null)]
    [InlineData("created|0|0001-01-01T00:00:00Z", CSharpFileStatus.Stopped, null)]
    public void ParsesContainerState(string inspected, CSharpFileStatus status, int? exitCode)
    {
        var state = DockerCSharpRunner.ParseState(inspected);

        Assert.Equal(status, state.Status);
        Assert.Equal(exitCode, state.ExitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }
}
