using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class CSharpFileNeuronFacts
{
    [Fact]
    public async Task CallersCannotBindAFileToAnInstalledApp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Brain(new FakeSandbox(), ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<ICSharpAppBinding>("file").BindApp("arbitrary-app"));
    }

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
        var start = Assert.Single(sandbox.Requests, FakeSandbox.IsStart);
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

        var runs = sandbox.Requests.Where(FakeSandbox.IsStart).ToArray();
        Assert.Equal(2, runs.Length);
        Assert.Single(sandbox.Requests, request => request.Path.EndsWith("/stop", StringComparison.Ordinal));
        Assert.Equal(1, await brain.Get<IFakeAspireProbe>(new CSharpOptions().AspireApplication).Starts());
    }

    [Fact]
    public async Task ACleanExitFinishesTheFileWithoutAnotherRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = await Started(brain, "workspace/once", ct);

        sandbox.ExitLatest(0);
        await Reconcile(file);

        var finished = await file.Read(ct);
        Assert.Equal(CSharpFileStatus.Exited, finished.Status);
        Assert.False(finished.ShouldRun);
        Assert.Equal(1, sandbox.Started);
    }

    [Fact]
    public async Task ACrashingScriptIsRetriedThenGivenUp()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = await Started(brain, "workspace/crash", ct);

        sandbox.ExitLatest(1);
        Assert.Equal(CSharpFileStatus.Restarting, (await file.Read(ct)).Status);
        for (var failure = 1; failure < CSharpFileNeuron.MaximumFailures; failure++)
        {
            await Reconcile(file);
            Assert.Equal(failure, (await file.Read(ct)).Failures);
            sandbox.ExitLatest(1);
        }
        await Reconcile(file);

        var abandoned = await file.Read(ct);
        Assert.False(abandoned.ShouldRun);
        Assert.Equal(CSharpFileStatus.Exited, abandoned.Status);
        Assert.Equal(CSharpFileNeuron.MaximumFailures, abandoned.Failures);
        Assert.Equal(CSharpFileNeuron.MaximumFailures, sandbox.Started);
    }

    [Fact]
    public async Task ARunLostWithItsContainerIsRestartedWithoutCountingAFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = await Started(brain, "workspace/lost", ct);

        sandbox.LoseEveryRun();
        await Reconcile(file);

        var restarted = await file.Read(ct);
        Assert.Equal(CSharpFileStatus.Running, restarted.Status);
        Assert.Equal(0, restarted.Failures);
        Assert.Equal(2, sandbox.Started);
    }

    [Fact]
    public async Task StoppingDisarmsTheReconcile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var file = await Started(brain, "workspace/stopped", ct);

        var stopped = await file.Stop(ct);
        await Reconcile(file);

        Assert.False(stopped.ShouldRun);
        Assert.Equal(CSharpFileStatus.Exited, (await file.Read(ct)).Status);
        Assert.Equal(1, sandbox.Started);
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

    private static async Task<ICSharpFile> Started(UnitBrain brain, string id, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(id);
        await file.Write("Console.WriteLine(1);", ct);
        await file.Start(ct);
        return file;
    }

    // Reminders fire at most once a minute, so tests deliver the reconcile tick directly.
    private static Task Reconcile(ICSharpFile file)
        => file.AsReference<IRemindable>().ReceiveReminder(CSharpFileNeuron.ReconcileReminder, default);

    private static Task<UnitBrain> Brain(FakeSandbox sandbox, CancellationToken ct) => SandboxBrain.StartAsync(sandbox, ct);
}
