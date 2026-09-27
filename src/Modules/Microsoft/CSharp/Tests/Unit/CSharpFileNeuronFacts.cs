using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CSharpFileNeuronFacts
{
    [Fact]
    public async Task StartsTheSandboxOnDemandAndRunsTheFileWithItsSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = brain.Get<ICSharpFile>("workspace/report");
        await using var changes = await brain.Observe<CSharpFileChanged>(file, ct);

        await file.Write("Console.WriteLine(\"hi\");", ct);
        await file.Configure(new Dictionary<string, string> { ["TimerId"] = "tea" }, ct);
        var running = await file.Start(ct);

        Assert.Equal(CSharpFileStatus.Running, running.Status);
        var start = Assert.Single(sandbox.Requests, request => request is { Method: "POST", Path: "runs" });
        Assert.Equal("Console.WriteLine(\"hi\");", start.Body!["source"]!.GetValue<string>());
        Assert.Equal("tea", start.Body["environment"]!["CSharpFile__Settings__TimerId"]!.GetValue<string>());
        Assert.Equal("workspace/report", (await changes.NextAsync(ct: ct)).FileId);
        Assert.Equal("hi", await file.ReadLogs(cancellationToken: ct));

        await file.Delete(ct);

        Assert.Contains(sandbox.Requests, request => request.Method == "POST" && request.Path.EndsWith("/stop", StringComparison.Ordinal));
        var deleted = await file.Read(ct);
        Assert.Equal(CSharpFileStatus.Stopped, deleted.Status);
        Assert.Equal("", deleted.Source);
    }

    [Fact]
    public async Task RestartingStopsThePreviousRunAndStartsTheSandboxOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = brain.Get<ICSharpFile>("workspace/restart");
        await file.Write("Console.WriteLine(1);", ct);

        await file.Start(ct);
        await file.Start(ct);

        var runs = sandbox.Requests.Where(request => request is { Method: "POST", Path: "runs" }).ToArray();
        Assert.Equal(2, runs.Length);
        Assert.Single(sandbox.Requests, request => request.Path.EndsWith("/stop", StringComparison.Ordinal));
        Assert.Equal(1, await brain.Get<IFakeAspireProbe>(new CSharpOptions().AspireApplication).Starts());
    }

    [Fact]
    public async Task RejectsStartWithoutSourceAndInvalidInput()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Brain(new FakeSandbox(), ct);
        var file = brain.Get<ICSharpFile>("empty");

        await Assert.ThrowsAsync<InvalidOperationException>(() => file.Start(ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Configure(new Dictionary<string, string> { ["bad name"] = "x" }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => file.Write(new string('x', CSharpFileNeuron.MaximumSourceBytes + 1), ct));
    }

    [Fact]
    public async Task AnUnsetSourceRootFallsBackToTheRepository()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpModule>().StartAsync(ct);

        var options = brain.SiloServices.GetRequiredService<IOptions<CSharpOptions>>().Value;

        Assert.Equal(CSharpModule.FindRepositoryRoot(), options.SourceRoot);
    }

    private static Task<UnitBrain> Brain(FakeSandbox sandbox, CancellationToken ct)
        => UnitTest.Create().WithModule<CSharpModule>()
            .ConfigureSilo(silo => silo.Services.AddHttpClient<SandboxCSharpRunner>().ConfigurePrimaryHttpMessageHandler(() => sandbox))
            .StartAsync(ct);
}
