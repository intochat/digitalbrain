using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests;

public sealed class CSharpSubscriptionFacts
{
    [Fact]
    public async Task OppositePublicationOrdersAcrossSubscriptionsHaveIdenticalWakeReplayPayloads()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);

        async Task<string[]> Replay(bool researchFirst)
        {
            var scope = "workspace/stop-order/" + researchFirst;
            var research = brain.Get<IPinger>(scope + "/research");
            var stop = brain.Get<IPinger>(scope + "/stop");
            var file = await Subscribed(brain, sandbox, scope, research, ct);
            var edge = file.AsReference<ICSharpFileEdge>();
            await edge.Subscribed(sandbox.LatestRunId, stop.GetGrainId().ToString(), nameof(Pinged));
            sandbox.ExitLatest(0);

            var first = researchFirst ? research : stop;
            var second = researchFirst ? stop : research;
            await first.Ping(1);
            await Eventually(async () => (await file.Read(ct)).Subscriptions.Single(s => s.Neuron == first.GetGrainId().ToString()).Pending == 1, ct);
            await second.Ping(1);
            await Eventually(async () => (await file.Read(ct)).Subscriptions.All(s => s.Pending == 1), ct);

            // A resumed Stop loop can drain first regardless of publication order.
            var stopped = Assert.Single(await edge.DrainPending(sandbox.LatestRunId, stop.GetGrainId().ToString(), nameof(Pinged)));
            var requested = Assert.Single(await edge.DrainPending(sandbox.LatestRunId, research.GetGrainId().ToString(), nameof(Pinged)));
            return [stopped, requested];
        }

        var staleResearch = await Replay(researchFirst: true);
        var freshResearch = await Replay(researchFirst: false);
        Assert.Equal(new[] { "{\"number\":1}", "{\"number\":1}" }, staleResearch);
        Assert.Equal(staleResearch, freshResearch);
    }

    [Fact]
    public async Task ASubscriptionIsRecordedOnceAndBuffersOnlyMatchingSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/buffering", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        // Registering the same subscription again is a no-op.
        await edge.Subscribed(sandbox.LatestRunId, pinger.GetGrainId().ToString(), nameof(Pinged));
        await pinger.Ping(7);

        await Eventually(async () => (await edge.DrainPending(sandbox.LatestRunId, pinger.GetGrainId().ToString(), nameof(Pinged))) is { Count: > 0 }, ct);
        Assert.Single((await file.Read(ct)).Subscriptions);
    }

    [Fact]
    public async Task DrainReturnsPendingSignalsOnceInArrivalOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/draining", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        await pinger.Ping(1);
        await pinger.Ping(2);
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Single().Pending == 2, ct);

        var drained = await edge.DrainPending(sandbox.LatestRunId, pinger.GetGrainId().ToString(), nameof(Pinged));
        Assert.Equal(2, drained.Count);
        Assert.Contains("\"number\":1", drained[0], StringComparison.Ordinal);
        Assert.Contains("\"number\":2", drained[1], StringComparison.Ordinal);
        Assert.Empty(await edge.DrainPending(sandbox.LatestRunId, pinger.GetGrainId().ToString(), nameof(Pinged)));
    }

    [Fact]
    public async Task TwoSubscriptionsKeepTheirSignalsApart()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var one = brain.Get<IPinger>("one");
        var two = brain.Get<IPinger>("two");
        var file = await Subscribed(brain, sandbox, "workspace/apart", one, ct);
        var edge = file.AsReference<ICSharpFileEdge>();
        await edge.Subscribed(sandbox.LatestRunId, two.GetGrainId().ToString(), nameof(Pinged));

        await two.Ping(9);
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Any(s => s.Pending > 0), ct);

        Assert.Empty(await edge.DrainPending(sandbox.LatestRunId, one.GetGrainId().ToString(), nameof(Pinged)));
        Assert.Single(await edge.DrainPending(sandbox.LatestRunId, two.GetGrainId().ToString(), nameof(Pinged)));
    }

    [Fact]
    public async Task PendingSignalsAreBoundedAndTheOldestAreDropped()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/bounded", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        for (var number = 0; number < CSharpFileNeuron.MaximumPending + 5; number++) { await pinger.Ping(number); }
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Single().Pending == CSharpFileNeuron.MaximumPending, ct);

        var drained = await edge.DrainPending(sandbox.LatestRunId, pinger.GetGrainId().ToString(), nameof(Pinged));
        Assert.Equal(CSharpFileNeuron.MaximumPending, drained.Count);
        Assert.Contains("\"number\":5", drained[0], StringComparison.Ordinal); // 0..4 were dropped
    }

    [Fact]
    public async Task SubscribingRequiresARunThatSpeaksForTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = brain.Get<ICSharpFile>("workspace/unauthorized");
        await file.Write("Console.WriteLine(1);", ct);
        // Never started: no run id speaks for this file.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => file.AsReference<ICSharpFileEdge>().Subscribed("not-a-run", pinger.GetGrainId().ToString(), nameof(Pinged)));
    }

    [Fact]
    public async Task ASignalWithNoLiveRunWakesOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/waking", pinger, ct);

        sandbox.ExitLatest(0);                        // the registering run ends cleanly
        await Reconcile(file);                        // reconcile observes the exit
        Assert.True((await file.Read(ct)).ShouldRun); // ...but a subscribed file is waiting, not finished

        await pinger.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);   // a wake-run started
    }

    [Fact]
    public async Task ASecondSignalDuringAWakeRunQueuesInsteadOfStartingAnotherRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/queueing", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await pinger.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);
        await pinger.Ping(2);                         // wake-run still running
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);

        Assert.Equal(2, sandbox.Started);             // no third run
        Assert.Equal(2, (await file.Read(ct)).Subscriptions.Single().Pending);
    }

    [Fact]
    public async Task ASubscribedFileStillWakesAfterItsActivationIsCollected()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/collected-sub", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await brain.DeactivateAsync(file, ct);
        await pinger.Ping(1);

        await Eventually(() => sandbox.Started == 2, ct);
    }

    [Fact]
    public async Task FailingWakeRunsClearTheSubscriptionsAndDisarm()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/failing-sub", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        for (var attempt = 1; attempt <= CSharpFileNeuron.MaximumFailures; attempt++)
        {
            await pinger.Ping(attempt);
            await Eventually(() => sandbox.Started == attempt + 1, ct);
            sandbox.ExitLatest(1);
        }
        await pinger.Ping(0);
        await Eventually(async () => !(await file.Read(ct)).ShouldRun, ct);

        Assert.Empty((await file.Read(ct)).Subscriptions);
        Assert.Equal(CSharpFileNeuron.MaximumFailures + 1, sandbox.Started);
    }

    [Fact]
    public async Task StoppingClearsSubscriptionsAndNothingWakes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/stopped-sub", pinger, ct);

        await file.Stop(ct);
        await pinger.Ping(1);
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);

        Assert.Empty((await file.Read(ct)).Subscriptions);
        Assert.Equal(1, sandbox.Started);             // only the original registering run ever started
    }

    [Fact]
    public async Task PendingLeftByALostRunIsRedeliveredByTheReconcile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/lost-sub", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await pinger.Ping(1);                         // buffered, wakes run 2; nothing drained it
        await Eventually(() => sandbox.Started == 2, ct);
        sandbox.LoseEveryRun();                       // the wake-run vanished with its container
        await Reconcile(file);

        await Eventually(() => sandbox.Started == 3, ct);   // the pending signal is not stranded
    }

    [Fact]
    public async Task AnArmedFileIgnoresSameTypedSignalsFromOtherWatchedSources()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var tea = brain.Get<IPinger>("tea");
        var other = brain.Get<IPinger>("other");
        var file = brain.Get<ICSharpFile>("workspace/mixed");
        await file.Write("Console.WriteLine(1);", ct);
        await file.Arm(new(tea.GetGrainId().ToString(), nameof(Pinged)), ct);
        await file.AsReference<ICSharpFileEdge>().Subscribed("triggered-run", other.GetGrainId().ToString(), nameof(Pinged));

        await other.Ping(9);
        await Eventually(() => sandbox.Started == 1, ct);
        await tea.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);

        var starts = sandbox.Requests.Where(FakeSandbox.IsStart).ToArray();
        Assert.Null(starts[0].Body!["environment"]!["CSharpFile__Trigger"]);   // other's ping woke, it did not fire the trigger
        Assert.Contains("\"number\":1", starts[1].Body!["environment"]!["CSharpFile__Trigger"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACleanWakeRunResetsTheFailureCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, sandbox, "workspace/recovering", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await pinger.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);
        sandbox.ExitLatest(1);                        // one crash...
        await pinger.Ping(2);
        await Eventually(() => sandbox.Started == 3, ct);
        sandbox.ExitLatest(0);                        // ...then a clean wake-run
        await pinger.Ping(3);
        await Eventually(() => sandbox.Started == 4, ct);

        Assert.Equal(0, (await file.Read(ct)).Failures);
    }

    [Fact]
    public async Task AScriptCannotSubscribeToItsOwnFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var file = brain.Get<ICSharpFile>("workspace/self");
        await file.Write("Console.WriteLine(1);", ct);
        await file.Start(ct);

        await Assert.ThrowsAsync<ArgumentException>(() => file.AsReference<ICSharpFileEdge>()
            .Subscribed(sandbox.LatestRunId, file.GetGrainId().ToString(), nameof(CSharpFileChanged)));
    }

    private static Task Reconcile(ICSharpFile file)
        => file.AsReference<IRemindable>().ReceiveReminder(CSharpFileNeuron.ReconcileReminder, default);

    // Starts the file (always-on) so a real run id exists, then registers the subscription as that run.
    internal static async Task<ICSharpFile> Subscribed(ModuleBrain brain, FakeSandbox sandbox, string id, IPinger source, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(id);
        await file.Write("Console.WriteLine(1);", ct);
        await file.Start(ct);
        await file.AsReference<ICSharpFileEdge>().Subscribed(sandbox.LatestRunId, source.GetGrainId().ToString(), nameof(Pinged));
        return file;
    }

    private static Task Eventually(Func<bool> condition, CancellationToken ct) => Eventually(() => Task.FromResult(condition()), ct);

    private static async Task Eventually(Func<Task<bool>> condition, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!await condition()) { await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token); }
    }
}
