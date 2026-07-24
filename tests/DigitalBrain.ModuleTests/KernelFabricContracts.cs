using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class KernelFabricContracts(ModuleFixture fixture)
{
    [Fact]
    public async Task OwnerAuthorizationRequiresTheTargetsLogicalOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var alice = test.Owner("alice");
        var bob = test.Owner("bob");
        var driver = alice.Neuron<IModuleDriver>("driver");
        var ownTarget = alice.Neuron<IProbeTarget>("target");
        var foreignTarget = bob.Neuron<IProbeTarget>("target");

        var ownResult = driver.Outgoing.NextAsync<AuthorizationObserved>(
            cancellationToken);
        await alice.Client.SendAsync<IModuleDriver>(
            "driver",
            new InvokeTarget(ownTarget.Id));

        var foreignResult = driver.Outgoing.NextAsync<AuthorizationObserved>(
            cancellationToken);
        await alice.Client.SendAsync<IModuleDriver>(
            "driver",
            new InvokeTarget(foreignTarget.Id));

        Assert.True((await ownResult).Synapse.Authorized);
        var rejected = (await foreignResult).Synapse;
        Assert.False(rejected.Authorized);
        Assert.Equal(nameof(NeuronAuthorizationException), rejected.Failure);
    }

    [Fact]
    public async Task InboundRetryCommitsExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var target = test.Neuron<IRollbackProbe>("dedupe");
        await using var fault = target.FailNextJournalCommit(
            "retry the delivery");
        var committed = target.Outgoing.NextAsync<Counted>(
            cancellationToken);

        await test.Client.SendAsync<IRollbackProbe>(
            "dedupe",
            new Increment());
        Assert.Equal(1, (await committed).Synapse.Count);

        Assert.Single(await target.Incoming.ReadAsync<Increment>(
            cancellationToken: cancellationToken));
        Assert.Single(await target.Outgoing.ReadAsync<Counted>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task IncomingAndOutgoingCursorsSurviveHostRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var target = test.Neuron<IProbeTarget>("durable-cursors");
        var firstCommitted = target.Outgoing.NextAsync<ProbePong>(
            cancellationToken);

        await test.Client.SendAsync<IProbeTarget>(
            "durable-cursors",
            new ProbePing("first"));
        var firstOutgoing = await firstCommitted;
        var firstIncoming = Assert.Single(
            await target.Incoming.ReadAsync<ProbePing>(
                cancellationToken: cancellationToken));
        var secondCommitted = target.Outgoing.NextAsync<ProbePong>(
            cancellationToken);
        await test.Client.SendAsync<IProbeTarget>(
            "durable-cursors",
            new ProbePing("second"));
        var secondOutgoing = await secondCommitted;
        await target.RestartHostAsync(cancellationToken);

        var incoming = Assert.Single(
            await target.Incoming.ReadAsync<ProbePing>(
                firstIncoming.Sequence,
                cancellationToken: cancellationToken));
        var outgoing = Assert.Single(
            await target.Outgoing.ReadAsync<ProbePong>(
                firstOutgoing.Sequence,
                cancellationToken: cancellationToken));
        Assert.Equal("second", incoming.Synapse.Value);
        Assert.Equal("second", outgoing.Synapse.Value);
        Assert.Equal(secondOutgoing.Sequence, outgoing.Sequence);
    }

    [Fact]
    public async Task BroadcastRoutesToEveryHandlerAndCyclesStopAtTheDepthBound()
    {
        const int maximumDepth = 16;
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var announcer = test.Neuron<IAnnouncer>("announcer");
        var cycleEntry = test.Neuron<ICycleProbe>("cycle");
        var noticePublished = announcer.Outgoing.NextAsync<Notice>(
            cancellationToken);
        var loopPublished = cycleEntry.Outgoing.NextAsync<LoopSignal>(
            cancellationToken);

        await test.Client.SendAsync<IAnnouncer>(
            "announcer",
            new Announce());
        var notice = await noticePublished;
        var listener = test.Neuron<INoticeListener>(
            notice.CorrelationId.Value.ToString("D"));
        var audit = test.Neuron<INoticeAudit>(
            notice.CorrelationId.Value.ToString("D"));
        var seen = listener.Outgoing.NextAsync<NoticeSeen>(
            cancellationToken);
        var audited = audit.Outgoing.NextAsync<NoticeAudited>(
            cancellationToken);
        await seen;
        await audited;

        await test.Client.SendAsync<ICycleProbe>(
            "cycle",
            new LoopSignal());
        var loop = await loopPublished;
        var cycle = test.Neuron<ICycleProbe>(
            loop.CorrelationId.Value.ToString("D"));
        var looped = cycle.Outgoing.NextAsync<LoopObserved>(
            cancellationToken);

        ObservedSynapse<LoopObserved> observation = await looped;
        for (var count = 2; count < maximumDepth; count++)
        {
            observation = await cycle.Outgoing.NextAsync<LoopObserved>(
                cancellationToken);
        }

        Assert.Equal(maximumDepth - 1, observation.Synapse.Count);
        Assert.Equal(
            maximumDepth - 1,
            (await cycle.Incoming.ReadAsync<LoopSignal>(
                cancellationToken: cancellationToken)).Count);
    }

    [Fact]
    public async Task PinnedNeuronsDeliverAcrossDistinctSiloLabels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var alpha = test.Neuron<IAlphaProbe>("alpha");
        var beta = test.Neuron<IBetaProbe>("beta");
        var arrived = beta.Outgoing.NextAsync<CrossSiloArrived>(
            cancellationToken);

        await test.Client.SendAsync<IAlphaProbe>(
            "alpha",
            new CrossSilo());

        await arrived;
        var beforeWait = beta.Outgoing.NextAsync<BetaMarker>(
            cancellationToken);
        await test.Client.SendAsync<IBetaProbe>(
            "beta",
            new ReadBetaMarker());
        var before = await beforeWait;

        await alpha.RestartHostAsync(cancellationToken);

        var afterWait = beta.Outgoing.NextAsync<BetaMarker>(
            cancellationToken);
        await test.Client.SendAsync<IBetaProbe>(
            "beta",
            new ReadBetaMarker());
        var after = await afterWait;
        var secondArrival = beta.Outgoing.NextAsync<CrossSiloArrived>(
            cancellationToken);
        await test.Client.SendAsync<IAlphaProbe>(
            "alpha",
            new CrossSilo());

        await secondArrival;
        Assert.Equal(before.Synapse.Activation, after.Synapse.Activation);
    }

    [Fact]
    public async Task JournalCommitFailureRollsBackTheSemanticTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("rollback-driver");
        var target = test.Neuron<IRollbackProbe>("rollback");
        await using var fault = target.FailJournalCommitAfter(
            1,
            "roll back the turn");
        var observed = driver.Outgoing.NextAsync<CountObserved>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "rollback-driver",
            new InvokeRollback(target.Id));

        Assert.Equal(1, (await observed).Synapse.Count);
        Assert.Single(await target.Outgoing.ReadAsync<Counted>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task DelegatedCapabilityCompletesAgainstItsCommittedRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        test.Chat().Reply("delegated result");
        var task = test.Neuron<ITask>("delegation-task");
        var worker = test.Neuron<IModuleGroupChat>("delegation-worker");
        var requested = worker.Outgoing.NextAsync<CapabilityRequested>(
            cancellationToken);
        var terminal = task.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "driver",
            new StartModuleTask(
                task.Id,
                new StartTask(
                    CommandId.New(),
                    new ModuleGoal("delegate"),
                    worker.Id,
                    new TaskPolicy(1, TimeSpan.Zero, null))));

        var request = await requested;
        var completed = await worker.Outgoing.NextAsync<CapabilityCompleted>(
            cancellationToken);
        var succeeded = await terminal;

        Assert.Equal(request.SynapseId, completed.Synapse.Request);
        Assert.Equal(worker.Id, succeeded.Synapse.Worker);
    }

    [Fact]
    public async Task WatchResumesFromItsCommittedCursor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var target = test.Neuron<IProbeTarget>("watch");
        var firstWait = target.Outgoing.NextAsync<ProbePong>(
            cancellationToken);

        await test.Client.SendAsync<IProbeTarget>(
            "watch",
            new ProbePing("first"));
        var first = await firstWait;

        var secondWait = target.Outgoing.NextAsync<ProbePong>(
            cancellationToken);
        await test.Client.SendAsync<IProbeTarget>(
            "watch",
            new ProbePing("second"));
        var second = await secondWait;

        Assert.Equal("second", second.Synapse.Value);
        Assert.True(second.Sequence > first.Sequence);
        var resumed = Assert.Single(
            await target.Outgoing.ReadAsync<ProbePong>(
                first.Sequence,
                cancellationToken));
        Assert.Equal(second.Sequence, resumed.Sequence);
    }
}
