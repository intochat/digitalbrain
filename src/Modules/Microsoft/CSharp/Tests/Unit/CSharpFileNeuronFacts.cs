using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Microsoft.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CSharpFileNeuronFacts
{
    [Fact]
    public async Task WritesConfiguresStartsAndDeletesThroughDocker()
    {
        var ct = TestContext.Current.CancellationToken;
        var docker = new FakeDocker();
        await using var brain = await Brain(docker, ct);
        var file = brain.Get<ICSharpFile>("workspace/report");
        await using var changes = await brain.Observe<CSharpFileChanged>(file, ct);

        await file.Write("Console.WriteLine(\"hi\");", ct);
        await file.Configure(new Dictionary<string, string> { ["TimerId"] = "tea", ["Account__mail"] = "gmail-1" }, ct);
        var running = await file.Start(ct);

        Assert.Equal(CSharpFileStatus.Running, running.Status);
        Assert.Equal("Console.WriteLine(\"hi\");", running.Source);
        var run = Assert.Single(docker.Calls, call => call[0] == "run");
        Assert.Contains("CSharpFile__Id=workspace/report", run);
        Assert.Contains("CSharpFile__Settings__TimerId=tea", run);
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
        await using var brain = await Brain(new FakeDocker(), ct);
        var file = brain.Get<ICSharpFile>("empty");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.Start(ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Configure(new Dictionary<string, string> { ["bad name"] = "x" }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Write(new string('x', CSharpFileNeuron.MaximumSourceBytes + 1), ct));
    }

    [Fact]
    public async Task UnsetComposedSettingsFallBackToTheRepositoryAndATemporaryRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpModule>().StartAsync(ct);

        var options = brain.SiloServices.GetRequiredService<IOptions<CSharpOptions>>().Value;

        Assert.Equal(CSharpModule.FindRepositoryRoot(), options.SourceRoot);
        Assert.False(string.IsNullOrWhiteSpace(options.Root));
    }

    private static Task<UnitBrain> Brain(FakeDocker docker, CancellationToken ct)
        => UnitTest.Create().WithModule<CSharpModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IProcessRunner>(docker))
            .StartAsync(ct);
}
