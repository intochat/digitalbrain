using System.Collections.Concurrent;
using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CSharpFileNeuronFacts
{
    [Fact]
    public async Task WritesConfiguresStartsAndDeletesThroughTheRunner()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeRunner();
        await using var brain = await UnitTest.Create().WithModule<CSharpModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICSharpRunner>(runner))
            .StartAsync(ct);
        var file = brain.Get<ICSharpFile>("workspace/report");
        await using var changes = await brain.Observe<CSharpFileChanged>(file, ct);

        await file.Write("Console.WriteLine(\"hi\");", ct);
        await file.Configure(new Dictionary<string, string> { ["TimerId"] = "tea", ["Account__mail"] = "gmail-1" }, ct);
        var running = await file.Start(ct);

        Assert.Equal(CSharpFileStatus.Running, running.Status);
        Assert.Equal("Console.WriteLine(\"hi\");", running.Source);
        var started = Assert.Single(runner.Started);
        Assert.Equal("workspace/report", started.FileId);
        Assert.Equal("tea", started.Settings["TimerId"]);
        Assert.Equal(CSharpFileStatus.Running, (await changes.NextAsync(change => change.Status == CSharpFileStatus.Running, ct)).Status);

        await file.Delete(ct);

        var deleted = await file.Read(ct);
        Assert.Equal(CSharpFileStatus.Stopped, deleted.Status);
        Assert.Equal("", deleted.Source);
        Assert.Empty(deleted.Settings);
    }

    [Fact]
    public async Task RejectsStartWithoutSourceAndInvalidInput()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<ICSharpRunner>(new FakeRunner()))
            .StartAsync(ct);
        var file = brain.Get<ICSharpFile>("empty");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.Start(ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Configure(new Dictionary<string, string> { ["bad name"] = "x" }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Write(new string('x', CSharpFileNeuron.MaximumSourceBytes + 1), ct));
    }

    private sealed class FakeRunner : ICSharpRunner
    {
        private readonly ConcurrentDictionary<string, CSharpContainerState> _containers = new();
        public ConcurrentQueue<(string FileId, IReadOnlyDictionary<string, string> Settings)> Started { get; } = new();

        public Task StartAsync(string fileId, string source, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
        {
            Started.Enqueue((fileId, settings));
            _containers[fileId] = new(CSharpFileStatus.Running, null, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task StopAsync(string fileId, CancellationToken cancellationToken)
        {
            _containers.TryRemove(fileId, out _);
            return Task.CompletedTask;
        }

        public Task<CSharpContainerState> InspectAsync(string fileId, CancellationToken cancellationToken)
            => Task.FromResult(_containers.GetValueOrDefault(fileId, CSharpContainerState.Missing));

        public Task<string> LogsAsync(string fileId, int tail, CancellationToken cancellationToken) => Task.FromResult("");
    }
}
