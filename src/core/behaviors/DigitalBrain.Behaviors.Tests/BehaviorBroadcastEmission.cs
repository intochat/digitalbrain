using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorBroadcastEmission(BroadcastSubscriptionFixture fixture)
{
    private const string EmittingBehavior = "com.digitalbrain.emitter";
    private const string SubscribingBehavior = "com.digitalbrain.subscriber";

    [Fact(DisplayName = "a behavior emits a fact its signed manifest declares and the emission is journaled", Timeout = 120_000)]
    public async Task DeclaredBroadcastEmitAliasIsEmittedAndJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var emitter = test.Neuron<IBehaviorNeuron>(EmittingBehavior);

        var active = await BehaviorInstall.ActivateAsync(test, emitter, BroadcastHarness.EmittingProgram());

        var emittedWait = emitter.Outgoing.NextAsync<BehaviorFactEmitted>(cancellationToken);
        var factWait = emitter.Outgoing.NextAsync<ProbeFactRaised>(cancellationToken);
        await emitter.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"spoken"}"""));

        var emitted = await emittedWait;
        var fact = await factWait;

        Assert.Equal(BroadcastHarness.DeclaredFactContractId, emitted.Synapse.EmitAlias);
        Assert.Equal(active.ActiveArtifactHash, emitted.Synapse.ArtifactHash);
        Assert.Equal("spoken", fact.Synapse.Label);
    }

    [Fact(DisplayName = "a behavior emitting a fact its manifest never declared is refused typed with no delivery", Timeout = 120_000)]
    public async Task UndeclaredBroadcastEmitAliasIsRefusedWithoutDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speechless = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var subscriber = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        // The subscribing program never calls EmitAsync, so its manifest carries no emit grants.
        await BehaviorInstall.ActivateAsync(test, speechless, BroadcastHarness.SubscribingProgram());
        await BehaviorInstall.ActivateAsync(test, subscriber, BroadcastHarness.SubscribingProgram());

        var refusedWait = speechless.Outgoing.NextAsync<BehaviorFactEmitRefused>(cancellationToken);
        await speechless.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"forbidden"}"""));

        var refused = await refusedWait;

        Assert.Equal("undeclared-broadcast-alias", refused.Synapse.Reason);
        Assert.Equal(BroadcastHarness.DeclaredFactContractId, refused.Synapse.AttemptedAlias);
        Assert.Empty(await speechless.Outgoing.ReadAsync<ProbeFactRaised>(cancellationToken: cancellationToken));
        Assert.Empty(await subscriber.Incoming.ReadAsync<ProbeFactRaised>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "an emission whose hop budget is spent is refused typed, with nothing delivered", Timeout = 120_000)]
    public async Task ExhaustedHopBudgetIsRefusedWithoutDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());
        await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var refusedWait = speaker.Outgoing.NextAsync<BehaviorFactEmitRefused>(cancellationToken);
        var outcome = await speaker.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"looped"}""")
        {
            HopsRemaining = 0,
        });

        var refused = await refusedWait;

        Assert.Equal(BehaviorFactEmission.HopBudgetExhausted, outcome);
        Assert.Equal(BehaviorFactEmission.HopBudgetExhausted, refused.Synapse.Reason);
        Assert.Empty(await speaker.Outgoing.ReadAsync<ProbeFactRaised>(
            afterSequence: 0,
            cancellationToken));
        Assert.Empty(await listener.Incoming.ReadAsync<ProbeFactRaised>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(DisplayName = "an emission that fails leaves no receipt behind, so the retry actually speaks", Timeout = 120_000)]
    public async Task FailedEmissionLeavesNoReceiptSoTheRetrySpeaks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);

        await BehaviorInstall.ActivateAsync(
            test,
            speaker,
            BroadcastHarness.EmittingProgram(BroadcastHarness.StalledOnceFactContractId));

        // The first subscriber lookup for this alias never answers, so EmitAsync throws before
        // anything reaches the outbox — the exact window a receipt written first would falsify.
        var command = new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.StalledOnceFactContractId,
            """{"label":"unspoken"}""");
        _ = await Assert.ThrowsAnyAsync<Exception>(() => speaker.Reference.EmitFact(command));

        Assert.Empty(await speaker.Outgoing.ReadAsync<ProbeFactStalledOnce>(
            afterSequence: 0,
            cancellationToken));

        var retried = await speaker.Reference.EmitFact(command);

        Assert.Equal(BehaviorFactEmission.Emitted, retried);
        Assert.Single(await speaker.Outgoing.ReadAsync<ProbeFactStalledOnce>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(DisplayName = "a refusal is not receipted, so the same command retried under a healthy condition speaks", Timeout = 120_000)]
    public async Task TransientRefusalDoesNotBlockALaterHealthyRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());

        // The hop budget is not part of the command identity, so receipting its refusal would
        // answer every later retry of the same request from a condition that no longer holds.
        var commandId = CommandId.New();
        var refused = await speaker.Reference.EmitFact(new EmitBehaviorFact(
            commandId,
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"retried"}""")
        {
            HopsRemaining = 0,
        });

        var emitted = await speaker.Reference.EmitFact(new EmitBehaviorFact(
            commandId,
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"retried"}"""));

        Assert.Equal(BehaviorFactEmission.HopBudgetExhausted, refused);
        Assert.Equal(BehaviorFactEmission.Emitted, emitted);
        Assert.Single(await speaker.Outgoing.ReadAsync<ProbeFactRaised>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(DisplayName = "the same emit command applied twice speaks the fact once", Timeout = 120_000)]
    public async Task RepeatedEmitCommandSpeaksTheFactOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var emitter = test.Neuron<IBehaviorNeuron>(EmittingBehavior);

        await BehaviorInstall.ActivateAsync(test, emitter, BroadcastHarness.EmittingProgram());

        var command = new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"once"}""");
        var first = await emitter.Reference.EmitFact(command);
        var second = await emitter.Reference.EmitFact(command);

        Assert.Equal(BehaviorFactEmission.Emitted, first);
        Assert.Equal(BehaviorFactEmission.Emitted, second);
        Assert.Single(await emitter.Outgoing.ReadAsync<ProbeFactRaised>(
            afterSequence: 0,
            cancellationToken));
        Assert.Single(await emitter.Outgoing.ReadAsync<BehaviorFactEmitted>(
            afterSequence: 0,
            cancellationToken));
    }

    [Fact(DisplayName = "emissions with neither a delivery turn nor a client entry bind to one correlation", Timeout = 120_000)]
    public async Task ActivationEmissionsShareOneBoundCorrelation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var pair = test.Neuron<IActivationPairEmitter>("pair");

        await pair.Reference.Touch();

        var head = await pair.Outgoing.ReadAsync<ProbeActivationHead>(
            afterSequence: 0,
            cancellationToken);
        var tail = await pair.Outgoing.ReadAsync<ProbeActivationTail>(
            afterSequence: 0,
            cancellationToken);

        Assert.Single(head);
        Assert.Single(tail);
        Assert.Equal(head[0].CorrelationId, tail[0].CorrelationId);
    }

    [Fact(DisplayName = "an emission binds its fact and its audit record to one correlation", Timeout = 120_000)]
    public async Task EmittedFactAndItsAuditShareOneCorrelation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var emitter = test.Neuron<IBehaviorNeuron>(EmittingBehavior);

        await BehaviorInstall.ActivateAsync(test, emitter, BroadcastHarness.EmittingProgram());

        var emittedWait = emitter.Outgoing.NextAsync<BehaviorFactEmitted>(cancellationToken);
        var factWait = emitter.Outgoing.NextAsync<ProbeFactRaised>(cancellationToken);
        await emitter.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"audited"}"""));

        var audit = await emittedWait;
        var fact = await factWait;

        Assert.Equal(fact.CorrelationId, audit.CorrelationId);
    }

    [Fact(DisplayName = "a behavior that both declares and speaks one fact is never woken by its own emission", Timeout = 120_000)]
    public async Task SelfEmittedFactNeverExecutesTheEmittingBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        // The speaker's trigger IS the fact it emits, so its manifest declares the alias on both sides.
        var speaking = await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.SelfEmittingProgram());
        await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var listenerWoke = listener.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await speaker.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"echo"}"""));

        // The listener waking is the barrier: the fact reached every subscriber of that alias.
        _ = await listenerWoke;

        Assert.NotNull(speaking.ActiveArtifactHash);
        Assert.Empty(await speaker.Outgoing.ReadAsync<BehaviorWokeOnFact>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "a wake starts a bound attempt whose activation identifies the woken behavior", Timeout = 120_000)]
    public async Task AWakeStartsABoundAttemptCarryingTheBehaviorActivation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());
        var listening = await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var wokeWait = listener.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await speaker.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"bound"}"""));

        var woke = await wokeWait;
        Assert.NotEqual(default, woke.Synapse.Attempt);

        var snapshot = await test.Cluster.Client
            .GetGrain<ITask>(woke.Synapse.Task.ToGrainId())
            .Read();

        Assert.Equal(woke.Synapse.Attempt, snapshot.ActiveAttempt);
        Assert.Equal(new BehaviorId(SubscribingBehavior), snapshot.Activation!.BehaviorId);
        Assert.Equal(listening.ActiveArtifactHash, snapshot.Activation.Revision.Value);
        var goal = Assert.IsType<BehaviorActivationGoal>(snapshot.Goal);
        Assert.Equal(snapshot.Activation.CaseId, goal.CaseId);
    }

    [Fact(DisplayName = "the budget a spoken fact was charged is the budget the woken attempt inherits", Timeout = 120_000)]
    public async Task TheWokenAttemptInheritsTheBudgetTheSpokenFactWasCharged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());
        await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var wokeWait = listener.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        await speaker.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"charged"}""")
        {
            HopsRemaining = 3,
        });

        var woke = await wokeWait;
        var snapshot = await test.Cluster.Client
            .GetGrain<ITask>(woke.Synapse.Task.ToGrainId())
            .Read();

        // Without the fact carrying its charged budget as a delivery depth the woken attempt
        // would start again at the ceiling, and an A-to-B-to-A chain would never terminate.
        var goal = Assert.IsType<BehaviorActivationGoal>(snapshot.Goal);
        Assert.Equal(3, goal.HopsRemaining);
        Assert.True(goal.HopsRemaining < BehaviorFactEmission.MaximumHops);
    }

    [Fact(DisplayName = "behavior B speaks a fact and behavior A wakes on it with B as the journaled cause", Timeout = 120_000)]
    public async Task BehaviorToBehaviorLoopClosesThroughTheVocabulary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());
        var listening = await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var wokeWait = listener.Outgoing.NextAsync<BehaviorWokeOnFact>(cancellationToken);
        var emittedWait = speaker.Outgoing.NextAsync<ProbeFactRaised>(cancellationToken);
        await speaker.Reference.EmitFact(new EmitBehaviorFact(
            CommandId.New(),
            BroadcastHarness.DeclaredFactContractId,
            """{"label":"self-extension"}"""));

        var spoken = await emittedWait;
        var woke = await wokeWait;
        var heard = await listener.Incoming.NextAsync<ProbeFactRaised>(cancellationToken);

        Assert.Equal("self-extension", heard.Synapse.Label);
        Assert.Equal(speaker.Id, heard.Caller);
        Assert.Equal(spoken.CorrelationId, heard.CorrelationId);
        Assert.Equal(heard.CorrelationId, woke.CorrelationId);
        Assert.Equal(listening.ActiveArtifactHash, woke.Synapse.ArtifactHash);
    }
}

internal static class BehaviorInstall
{
    internal static async Task<BehaviorSnapshot> ActivateAsync(
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
            "Broadcast",
            "Broadcast behavior"));
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
