using System.Net;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Microsoft.DotNet;
using Orleans.Configuration;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace DigitalBrain.Tests;

public sealed class DockerCSharpRunnerFacts : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "csharp-runner-facts", Guid.NewGuid().ToString("N"));

    private DockerCSharpRunner Runner(FakeDocker docker, string advertised = "127.0.0.1")
        => new(docker, Create(new CSharpOptions { Root = _root, SourceRoot = @"E:\repo" }),
            Create(new EndpointOptions { AdvertisedIPAddress = IPAddress.Parse(advertised), GatewayPort = 30000 }),
            Create(new ClusterOptions { ClusterId = "cluster", ServiceId = "service" }));

    [Fact]
    public async Task StartStopsTheOldContainerAndRunsTheFileWithSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        var docker = new FakeDocker();

        await Runner(docker).StartAsync("workspace/timer", "Console.WriteLine(1);", new Dictionary<string, string> { ["Greeting"] = "a = b c" }, ct);

        var calls = docker.Calls.ToArray();
        var container = DockerCSharpRunner.ContainerName("workspace/timer");
        Assert.Equal(["stop", "--time", "10", container], calls[0]);
        Assert.Equal(["rm", "--force", container], calls[1]);
        var run = calls[2];
        Assert.Equal(["run", "--detach", "--name", container], run.Take(4));
        Assert.Contains("on-failure:5", run);
        Assert.Contains(@"E:\repo:/brain:ro", run);
        Assert.Contains("CSharpFile__Settings__Greeting=a = b c", run);
        Assert.Contains("CSharpFile__Id=workspace/timer", run);
        Assert.Contains("ClusterId=cluster", run);
        Assert.Equal([DockerCSharpRunner.Image, "dotnet", "run", "app.cs", "-p:ArtifactsPath=/work/artifacts"], run.TakeLast(5));
        var work = Path.Combine(_root, container);
        Assert.Equal("Console.WriteLine(1);", await File.ReadAllTextAsync(Path.Combine(work, "app.cs"), ct));
        Assert.Contains(DockerCSharpRunner.ClientProject, await File.ReadAllTextAsync(Path.Combine(work, "Directory.Build.props"), ct), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("127.0.0.1", "gwy.tcp://127.0.0.1:30000/0", true)]
    [InlineData("10.1.2.3", "gwy.tcp://10.1.2.3:30000/0", false)]
    public async Task ContainersDialTheAdvertisedSiloAndRelayOnlyToALoopbackOne(string advertised, string gateway, bool relayed)
    {
        var docker = new FakeDocker();

        await Runner(docker, advertised).StartAsync("relay", "x", new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        var run = docker.Calls.Single(call => call[0] == "run");
        Assert.Contains("Gateways=" + gateway, run);
        Assert.Equal(relayed, run.Contains("GatewayRelayHost=host.docker.internal"));
    }

    [Fact]
    public async Task StoppingAMissingContainerIsNotAnError()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = Runner(new FakeDocker());

        await runner.StopAsync("never-started", ct);

        Assert.Equal(CSharpFileStatus.Stopped, (await runner.InspectAsync("never-started", ct)).Status);
    }

    [Fact]
    public async Task FailedDockerRunSurfacesTheDaemonError()
    {
        var docker = new FakeDocker(arguments => arguments[0] == "run" ? new ProcessResult(125, "", "Cannot connect to the Docker daemon", TimeSpan.Zero, false) : null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner(docker).StartAsync("a", "x", new Dictionary<string, string>(), TestContext.Current.CancellationToken));

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
