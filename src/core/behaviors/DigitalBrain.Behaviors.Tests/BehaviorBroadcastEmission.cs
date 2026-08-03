using DigitalBrain.Abstractions;
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

    [Fact(DisplayName = "behavior B speaks a fact and behavior A wakes on it with B as the journaled cause", Timeout = 120_000)]
    public async Task BehaviorToBehaviorLoopClosesThroughTheVocabulary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var speaker = test.Neuron<IBehaviorNeuron>(EmittingBehavior);
        var listener = test.Neuron<IBehaviorNeuron>(SubscribingBehavior);

        await BehaviorInstall.ActivateAsync(test, speaker, BroadcastHarness.EmittingProgram());
        var listening = await BehaviorInstall.ActivateAsync(test, listener, BroadcastHarness.SubscribingProgram());

        var wokeWait = listener.Outgoing.NextAsync<BehaviorExecuted>(cancellationToken);
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
