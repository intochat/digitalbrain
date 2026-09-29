using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CSharpSubscriptionFacts
{
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

    // Starts the file (always-on) so a real run id exists, then registers the subscription as that run.
    internal static async Task<ICSharpFile> Subscribed(UnitBrain brain, FakeSandbox sandbox, string id, IPinger source, CancellationToken ct)
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
