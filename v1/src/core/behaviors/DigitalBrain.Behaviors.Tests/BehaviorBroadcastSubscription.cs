using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Orleans.Concurrency;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorBroadcastSubscription(BroadcastSubscriptionFixture fixture)
{
    private const string SubscribingBehavior = "com.digitalbrain.subscriber";
    private const string WakeCaseId = "case.ProbeFactRaised";

    [Fact(DisplayName = "activated behavior declaring an event alias wakes on the module broadcast under one correlation", Timeout = 120_000)]
    public async Task DeclaredEventAliasWakesTheBehaviorOnBroadcast()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        var active = await InstallAsync(test, behavior, BroadcastHarness.SubscribingProgram());
        Assert.Equal(BehaviorRevisionStatus.Active, active.Status);

        var executedWait = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastDeclared("hello");

        var executed = await executedWait;
        var woken = await behavior.Incoming.NextAsync<ProbeFactRaised>(cancellationToken);

        Assert.Equal(active.ActiveArtifactHash, executed.Synapse.ArtifactHash);
        Assert.Equal("hello", woken.Synapse.Label);
        Assert.Equal(emitter.Id, woken.Caller);
        Assert.Equal(woken.CorrelationId, executed.CorrelationId);
    }

    [Fact(DisplayName = "a broadcast the manifest does not declare never reaches the behavior", Timeout = 120_000)]
    public async Task UndeclaredBroadcastNeverReachesTheBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        await InstallAsync(test, behavior, BroadcastHarness.SubscribingProgram());

        var executedWait = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastUndeclared("ignored");
        await emitter.Reference.BroadcastDeclared("observed");
        _ = await executedWait;

        var unwanted = await behavior.Incoming.ReadAsync<ProbeFactUnwanted>(
            cancellationToken: cancellationToken);
        var declared = await behavior.Incoming.ReadAsync<ProbeFactRaised>(
            cancellationToken: cancellationToken);

        Assert.Empty(unwanted);
        Assert.Single(declared);
        Assert.Equal("observed", declared[0].Synapse.Label);
    }

    [Fact(DisplayName = "stopping a behavior unregisters its subscriptions; restarting restores them", Timeout = 120_000)]
    public async Task StoppingUnregistersSubscriptionsAndStartingRestoresThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        await InstallAsync(test, behavior, BroadcastHarness.SubscribingProgram());
        await behavior.Reference.StopRun(new StopBehavior(CommandId.New()));

        await emitter.Reference.BroadcastDeclared("while-stopped");
        await behavior.Reference.StartRun(new StartBehavior(CommandId.New()));

        var executedWait = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastDeclared("after-restart");
        _ = await executedWait;

        var delivered = await behavior.Incoming.ReadAsync<ProbeFactRaised>(
            cancellationToken: cancellationToken);

        Assert.Single(delivered);
        Assert.Equal("after-restart", delivered[0].Synapse.Label);
    }

    [Fact(DisplayName = "a trigger alias the active catalog never broadcasts grants no subscription", Timeout = 120_000)]
    public async Task TriggerAliasOutsideTheCatalogGrantsNoSubscription()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var subscriber = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var outsider = test.Neuron<IBehaviorNeuron>("com.digitalbrain.outsider");
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        await InstallAsync(test, subscriber, BroadcastHarness.SubscribingProgram());
        await InstallAsync(test, outsider, BroadcastHarness.SubscribingProgram("behaviors.no-such-fact"));

        var executedWait = subscriber.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastDeclared("only-subscriber");
        _ = await executedWait;

        Assert.Empty(await outsider.Incoming.ReadAsync<ProbeFactRaised>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "a rehydrating behavior republishes its subscriptions over a diverged registry", Timeout = 120_000)]
    public async Task RehydrateRepublishesSubscriptionsOverADivergedRegistry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        await InstallAsync(test, behavior, BroadcastHarness.SubscribingProgram());

        var registry = test.Cluster.Client.GetGrain<IBehaviorSubscriptionRegistry>(
            BehaviorSubscriptionRegistry.ForOwner(test.Client.Owner).ToGrainId());
        await registry.Replace(behavior.Id.Name, [], cancellationToken);
        Assert.Empty(await registry.SubscribersOf(
            BroadcastHarness.DeclaredFactContractId,
            cancellationToken));

        await behavior.RestartHostAsync(cancellationToken);

        // Any call rehydrates the behavior, which is where the repair has to run.
        _ = await behavior.Reference.Read();

        Assert.Equal(
            [behavior.Id.Name],
            await registry.SubscribersOf(BroadcastHarness.DeclaredFactContractId, cancellationToken));

        var executedWait = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastDeclared("after-rehydrate");
        _ = await executedWait;
    }

    // A wake turn that throws anywhere after ITask.Start returns is retracted, but the Task grain is
    // not retracted with it. The outbox then redelivers the same fact, and the wake rebuilds its
    // whole Start payload from scratch — trigger included. This drives that rebuild directly.
    [Fact(DisplayName = "a wake redelivered after its turn was retracted rebuilds the identical Start payload and gets the task's receipt", Timeout = 120_000)]
    public async Task ARedeliveredWakeRebuildsTheStartPayloadTheTaskAlreadyReceipted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var owner = test.Client.Owner;
        var behaviorId = new BehaviorId(SubscribingBehavior);
        var revision = new BehaviorRevisionId(new string('a', 64));
        var wake = CommandId.New();
        var task = test.Neuron<ITask>($"wake-{wake.Value:N}");
        var worker = NeuronId.For<IWorker>(owner, $"wake-{wake.Value:N}");
        var triggers = test.Cluster.Client.GetGrain<IBehaviorProtectedTriggerGrain>(owner.Value);

        var first = await StartWakeAsync(triggers, task, worker, owner, behaviorId, revision, wake, cancellationToken);
        var redelivered = await StartWakeAsync(triggers, task, worker, owner, behaviorId, revision, wake, cancellationToken);

        Assert.Equal(first.ActiveAttempt, redelivered.ActiveAttempt);
        Assert.Equal(first.Revision, redelivered.Revision);
    }

    private static async Task<TaskSnapshot> StartWakeAsync(
        IBehaviorProtectedTriggerGrain triggers,
        TestNeuron<ITask> task,
        NeuronId worker,
        OwnerId owner,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        CommandId wake,
        CancellationToken cancellationToken)
    {
        var trigger = await triggers.StoreAsync(
            task.Id,
            behavior,
            revision,
            WakeCaseId,
            System.Text.Encoding.UTF8.GetBytes("""{"label":"redelivered"}"""),
            cancellationToken);

        return await task.Reference.Start(new StartTask(
            wake,
            new BehaviorActivationGoal(behavior, revision, "1", WakeCaseId, trigger, "ProbeFactRaised", []),
            worker,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: new BehaviorTaskActivation(
                behavior, revision, "1", WakeCaseId, trigger, "ProbeFactRaised", [])));
    }

    // The rail version of the same invariant. A wake starts its attempt through ITask.Start, which
    // is a capability request, so the kernel commits the fact as this behavior's inbound cause
    // before journaling it. The wake then emits BehaviorWokeOnFact, and a subscriber lookup that
    // fails there retracts the whole turn. Nothing may be lost by that: the outbox redelivers the
    // fact, the wake runs to the end, and the behavior stays awake for later broadcasts.
    [Fact(DisplayName = "a wake turn retracted after starting its attempt is redelivered and the behavior keeps waking", Timeout = 120_000)]
    public async Task ARetractedWakeIsRedeliveredAndTheBehaviorKeepsWaking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var behavior = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        await InstallAsync(test, behavior, BroadcastHarness.SubscribingProgram());

        var retractedWake = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        WakeFactLookupFault.FailNextWakeLookup(test.Client.Owner);
        await emitter.Reference.BroadcastDeclared("retracted");
        var recovered = await retractedWake;

        var laterWake = behavior.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await emitter.Reference.BroadcastDeclared("after-retraction");
        _ = await laterWake;

        var heard = await behavior.Incoming.ReadAsync<ProbeFactRaised>(cancellationToken: cancellationToken);

        // Two facts, two wakes, and the retracted turn's cause journaled exactly once.
        Assert.Equal(["retracted", "after-retraction"], heard.Select(fact => fact.Synapse.Label));
        Assert.Equal(heard[0].CorrelationId, recovered.CorrelationId);
    }

    [Fact(DisplayName = "a subscriber lookup that never answers fails the emit loudly instead of dropping it", Timeout = 120_000)]
    public async Task StalledSubscriberLookupFailsTheEmitLoudly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var emitter = test.Neuron<IBroadcastProbeEmitter>("probe");

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => emitter.Reference.BroadcastStalled("unanswered"));

        Assert.Contains(BroadcastHarness.StalledFactContractId, failure.Message, StringComparison.Ordinal);
        Assert.Empty(await emitter.Outgoing.ReadAsync<ProbeFactStalled>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(DisplayName = "a subscription publish that never answers fails loud instead of hanging activation")]
    public async Task StalledSubscriptionPublishFailsLoud()
    {
        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => BehaviorSubscriptionRegistry.WithinBoundAsync(
                token => Task.Delay(Timeout.InfiniteTimeSpan, token),
                nameof(IBehaviorSubscriptionRegistry.Replace),
                TimeSpan.FromMilliseconds(50),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            nameof(IBehaviorSubscriptionRegistry.Replace),
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the publish bound is the same registry bound the lookup already carries")]
    public void PublishAndLookupShareOneRegistryBound()
    {
        Assert.True(DeliveryPolicy.SubscriptionRegistryTimeout > TimeSpan.Zero);
        Assert.True(DeliveryPolicy.SubscriptionRegistryTimeout < DeliveryPolicy.DeliveryAttemptTimeout);
    }

    [Fact(DisplayName = "the subscription registry lookup interleaves and its write does not")]
    public void OnlyTheRegistryLookupInterleaves()
    {
        var lookup = typeof(IBehaviorSubscriptionRegistry)
            .GetMethod(nameof(IBehaviorSubscriptionRegistry.SubscribersOf))!;
        var write = typeof(IBehaviorSubscriptionRegistry)
            .GetMethod(nameof(IBehaviorSubscriptionRegistry.Replace))!;

        Assert.True(lookup.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true));
        Assert.False(write.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true));
    }

    [Fact(DisplayName = "the interleaving registry is a plain durable grain, so the serialized-turn guard is untouched")]
    public void TheInterleavingRegistryIsNotANeuron()
    {
        Assert.False(typeof(Neuron).IsAssignableFrom(typeof(BehaviorSubscriptionRegistryGrain)));

        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(BehaviorSubscriptionRegistryGrain)));

        Assert.Contains(nameof(AlwaysInterleaveAttribute), refusal.Message, StringComparison.Ordinal);
    }

    private static async Task<BehaviorSnapshot> InstallAsync(
        TestBrain test,
        TestNeuron<IBehaviorNeuron> behavior,
        string program)
    {
        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            program,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["install"] = BroadcastHarness.SubscribingFeature,
            },
            "Subscriber",
            "Subscribing behavior"));
        Assert.Equal(BehaviorRevisionStatus.Proposed, proposed.Status);

        await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));

        var approval = new BehaviorRevisionApproval(
            Guid.NewGuid(),
            CommandId.New(),
            proposed.ProposedArtifactHash!,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);
        var deliveryWait = behavior.Incoming.NextAsync<BehaviorRevisionApproval>(
            TestContext.Current.CancellationToken);
        await test.Client.SendAsync(behavior.Id, approval, TestContext.Current.CancellationToken);
        _ = await deliveryWait;
        await behavior.Reference.Approve(approval);

        return await behavior.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
    }
}

public sealed class BroadcastSubscriptionFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<BehaviorBroadcastHarnessModule>();
    }
}
