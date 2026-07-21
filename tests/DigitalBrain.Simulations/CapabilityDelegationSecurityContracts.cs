using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Orleans.Concurrency;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class CapabilityDelegationSecurityContracts
{
    private const int MaximumRememberedDelegations = 32;
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "delegation mint and target entry observe completed atomic journal writes")]
    public async Task DelegationMintAndTargetEntryObserveCompletedAtomicJournalWrites()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-storage-ordering");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var issuerWrites = SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId());
        var targetWrites = SimulationCluster.CompletedJournalWrites(targetId.ToGrainId());

        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        Assert.Equal(
            issuerWrites + 1,
            SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId()));
        await issuer.DeactivateAsync();

        var durableOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(durableOutgoing.Delta, Is<CapabilityRequested>);
        DelegatedTargetStorageObservations.Expect(owner, targetId.ToGrainId(), targetWrites);
        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
    }

    [Fact(DisplayName = "a failed target journal write prevents semantic method entry")]
    public async Task FailedTargetJournalWritePreventsSemanticMethodEntry()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-target-write-failure");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        DelegatedTargetEntryObservations.Reset(owner);
        SimulationCluster.FailJournalWriteAfter(
            targetId.ToGrainId(),
            completedWritesBeforeFailure: 0,
            "injected target incoming persistence failure");

        try
        {
            _ = await Assert.ThrowsAnyAsync<Exception>(
                () => runner.InvokeAsync(delegation, targetId));
        }
        finally
        {
            SimulationCluster.ClearJournalWriteFailure(targetId.ToGrainId());
        }

        Assert.Equal(0, DelegatedTargetEntryObservations.Count(owner));
    }

    [Fact(DisplayName = "mismatched delegated calls are rejected without consuming the valid delegation")]
    public async Task MismatchedDelegatedCallsDoNotConsumeTheValidDelegation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-mismatches");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var wrongTarget = NeuronId.For<DelegatedCapabilityTarget>(owner, "other-target");
        var foreignTarget = NeuronId.For<DelegatedCapabilityTarget>(new OwnerId("foreign"), "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var wrongRunner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/wrong-runner"));
        var foreignRunner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create("foreign/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => wrongRunner.InvokeAsync(delegation, targetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => foreignRunner.InvokeAsync(delegation, targetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(delegation, wrongTarget));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(delegation, foreignTarget));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeFailureAsync(delegation, targetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAlternateAsync(delegation, targetId));

        var beforeAuthorizedUse = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(beforeAuthorizedUse.Delta, Is<CapabilityRequested>);
        Assert.DoesNotContain(beforeAuthorizedUse.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(beforeAuthorizedUse.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(beforeAuthorizedUse.Delta, Is<CapabilityRejected>);
        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));

        var targetIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0);

        Assert.Single(targetIncoming.Delta, Is<CapabilityRequested>);
    }

    [Fact(DisplayName = "minting rejects foreign runner and target owners before appending a request")]
    public async Task MintingRejectsForeignOwnersBeforeAppendingARequest()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-mint-owner");
        var foreignOwner = new OwnerId("delegation-mint-foreign");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var localTarget = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var foreignTarget = NeuronId.For<DelegatedCapabilityTarget>(foreignOwner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var localRunner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var foreignRunner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{foreignOwner.Value}/runner"));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => issuer.IssueAsync(
                foreignRunner.GetGrainId(),
                localTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => issuer.IssueAsync(
                localRunner.GetGrainId(),
                foreignTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));

        var outgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Empty(outgoing.Delta);
    }

    [Fact(DisplayName = "a forged raw RequestContext delivery cannot authorize a runner")]
    public async Task ForgedRawRequestContextDoesNotConsumeAValidDelegation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-forged-context");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);
        var request = Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeForgedAsync(request, targetId));

        var afterForgery = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(afterForgery.Delta, Is<CapabilityRequested>);
        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
    }

    [Fact(DisplayName = "a consumed delegation remains rejected after causal-caller reactivation")]
    public async Task ConsumedDelegationIsRejectedAfterCausalCallerReactivation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-replay-reactivation");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
        await issuer.DeactivateAsync();

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(delegation, targetId));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);
        var targetIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityFailed>);
        Assert.Single(targetIncoming.Delta, Is<CapabilityRequested>);
    }

    [Fact(DisplayName = "a consumed delegation is rejected on immediate same-activation replay")]
    public async Task ConsumedDelegationIsRejectedOnImmediateReplay()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-immediate-replay");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(delegation, targetId));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityFailed>);
    }

    [Fact(DisplayName = "delegation consumption is durable before semantic target invocation")]
    public async Task DelegationConsumptionIsDurableBeforeSemanticTargetInvocation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-consume-before-entry");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        DelegatedInvocationGate.Reset(owner);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.BlockAsync));

        var firstInvocation = runner.InvokeBlockedAsync(delegation, targetId);
        await DelegatedInvocationGate
            .Entered(owner)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await issuer.DeactivateAsync();

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner
                .InvokeBlockedAsync(delegation, targetId)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Equal(1, DelegatedInvocationGate.EntryCount(owner));
        DelegatedInvocationGate.Release(owner);
        Assert.Equal(23, await firstInvocation);

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
    }

    [Fact(DisplayName = "the persisted post-redemption loss cut requires a fresh delegation")]
    public async Task PersistedPostRedemptionLossCutRequiresFreshDelegation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-redeemed-loss-cut");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var consumed = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await runner.RedeemOnlyAsync(consumed, issuerId);
        await issuer.DeactivateAsync();

        var afterCrashCut = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);
        var targetBeforeRecovery = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0);

        Assert.Single(afterCrashCut.Delta, Is<CapabilityRequested>);
        Assert.DoesNotContain(afterCrashCut.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(afterCrashCut.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(afterCrashCut.Delta, Is<CapabilityRejected>);
        Assert.Empty(targetBeforeRecovery.Delta);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(consumed, targetId));

        var fresh = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        Assert.Equal(17, await runner.InvokeAsync(fresh, targetId));

        var recovered = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(2, recovered.Delta.Count(Is<CapabilityRequested>));
        Assert.Single(recovered.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(recovered.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(recovered.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "real authority callbacks reject the wrong runner and causal-caller target")]
    public async Task RealAuthorityCallbacksRejectWrongRunnerAndCausalCallerTarget()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-authority-identities");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var wrongIssuerId = NeuronId.For<DelegationIssuer>(owner, "wrong-issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var wrongRunner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/wrong-runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        var wrongRunnerFailure = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => wrongRunner.RedeemOnlyAsync(delegation, issuerId));
        var wrongCallerFailure = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RedeemOnlyAsync(delegation, wrongIssuerId));

        Assert.Contains("authority callback", wrongRunnerFailure.Message, StringComparison.Ordinal);
        Assert.Contains("authority callback", wrongCallerFailure.Message, StringComparison.Ordinal);
        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
    }

    [Fact(DisplayName = "duplicate matching outcomes are idempotent and contradictory outcomes are rejected")]
    public async Task DuplicateAndContradictoryDelegatedOutcomesCannotCreateExtraFacts()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-terminal-idempotency");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));
        await runner.RepeatOutcomeAsync(delegation, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(delegation, issuerId, succeeded: false));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityFailed>);
    }

    [Fact(DisplayName = "delegation retention backpressures when only protected active and terminal authority remains")]
    public async Task DelegationRetentionIsBoundedWithoutEvictingActiveAuthority()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-bounded-retention");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var initialTarget = NeuronId.For<DelegatedCapabilityTarget>(owner, "initial-target");
        var recoveryTarget = NeuronId.For<DelegatedCapabilityTarget>(owner, "recovery-target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var consumed = await issuer.IssueAsync(
            runner.GetGrainId(),
            initialTarget,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        await runner.RedeemOnlyAsync(consumed, issuerId);

        CapabilityDelegation? earliestIssued = null;

        for (var index = 1; index < MaximumRememberedDelegations; index++)
        {
            var issued = await issuer.IssueAsync(
                runner.GetGrainId(),
                initialTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync));
            earliestIssued ??= issued;
        }

        await issuer.DeactivateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => issuer.IssueAsync(
                runner.GetGrainId(),
                recoveryTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(consumed, initialTarget));

        var beforeTerminalCapacity = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(
            MaximumRememberedDelegations,
            beforeTerminalCapacity.Delta.Count(Is<CapabilityRequested>));
        Assert.Equal(17, await runner.InvokeAsync(earliestIssued!, initialTarget));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => issuer.IssueAsync(
                runner.GetGrainId(),
                recoveryTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));
        await runner.RepeatOutcomeAsync(earliestIssued!, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(earliestIssued!, issuerId, succeeded: false));

        var afterProtectedBackpressure = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(
            MaximumRememberedDelegations,
            afterProtectedBackpressure.Delta.Count(Is<CapabilityRequested>));
        Assert.Single(afterProtectedBackpressure.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(afterProtectedBackpressure.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(afterProtectedBackpressure.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "consumed delegation loss recovery stays bounded and protects the newest authority")]
    public async Task ConsumedDelegationLossRecoveryStaysBoundedAndProtectsTheNewestAuthority()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-consumed-retention");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var consumed = new List<CapabilityDelegation>();

        for (var index = 0; index < MaximumRememberedDelegations + 3; index++)
        {
            var delegation = await issuer.IssueAsync(
                runner.GetGrainId(),
                targetId,
                nameof(IDelegatedCapabilityTarget.EnterAsync));
            await runner.RedeemOnlyAsync(delegation, issuerId);
            consumed.Add(delegation);
        }

        await issuer.DeactivateAsync();

        var fresh = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(consumed[0], targetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(consumed[0], issuerId, succeeded: true));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAsync(consumed[^1], targetId));

        await runner.RepeatOutcomeAsync(consumed[^1], issuerId, succeeded: true);
        await runner.RepeatOutcomeAsync(consumed[^1], issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(consumed[^1], issuerId, succeeded: false));

        Assert.Equal(17, await runner.InvokeAsync(fresh, targetId));

        await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(consumed[^1], issuerId, succeeded: true));
        await runner.RepeatOutcomeAsync(fresh, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(fresh, issuerId, succeeded: false));
        await runner.RepeatOutcomeAsync(consumed[4], issuerId, succeeded: true);

        var journal = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(
            MaximumRememberedDelegations + 5,
            journal.Delta.Count(Is<CapabilityRequested>));
        Assert.Equal(3, journal.Delta.Count(Is<CapabilityCompleted>));
        Assert.DoesNotContain(journal.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(journal.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "capacity backpressure protects the newest terminal delegation")]
    public async Task CapacityBackpressureProtectsTheNewestTerminalDelegation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-terminal-retention");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var blockedTarget = NeuronId.For<DelegatedCapabilityTarget>(owner, "blocked-target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));

        for (var index = 0; index < MaximumRememberedDelegations - 1; index++)
        {
            await issuer.IssueAsync(
                runner.GetGrainId(),
                targetId,
                nameof(IDelegatedCapabilityTarget.EnterAsync));
        }

        var terminal = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        Assert.Equal(17, await runner.InvokeAsync(terminal, targetId));

        await issuer.DeactivateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => issuer.IssueAsync(
                runner.GetGrainId(),
                blockedTarget,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));
        await runner.RepeatOutcomeAsync(terminal, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(terminal, issuerId, succeeded: false));

        var journal = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(
            MaximumRememberedDelegations,
            journal.Delta.Count(Is<CapabilityRequested>));
        Assert.Single(journal.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(journal.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(journal.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "finishing atomically moves delegation retention from consumed to terminal")]
    public async Task FinishingAtomicallyMovesDelegationRetentionFromConsumedToTerminal()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-finish-retention-move");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var terminal = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        var beforeRedeem = SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId());
        await runner.RedeemOnlyAsync(terminal, issuerId);
        Assert.Equal(
            beforeRedeem + 1,
            SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId()));

        var beforeFinish = SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId());
        await runner.RepeatOutcomeAsync(terminal, issuerId, succeeded: true);
        Assert.Equal(
            beforeFinish + 1,
            SimulationCluster.CompletedJournalWrites(issuerId.ToGrainId()));
        await issuer.DeactivateAsync();

        var consumed = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        await runner.RedeemOnlyAsync(consumed, issuerId);

        for (var index = 2; index < MaximumRememberedDelegations; index++)
        {
            await issuer.IssueAsync(
                runner.GetGrainId(),
                targetId,
                nameof(IDelegatedCapabilityTarget.EnterAsync));
        }

        await issuer.DeactivateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => issuer.IssueAsync(
                runner.GetGrainId(),
                targetId,
                nameof(IDelegatedCapabilityTarget.EnterAsync)));
        await runner.RepeatOutcomeAsync(terminal, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(terminal, issuerId, succeeded: false));
    }

    [Fact(DisplayName = "persisted terminal order drives eviction before protected consumed authority")]
    public async Task PersistedTerminalOrderDrivesEvictionBeforeProtectedConsumedAuthority()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-persisted-terminal-retention");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var oldTerminal = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await runner.RedeemOnlyAsync(oldTerminal, issuerId);
        await runner.RepeatOutcomeAsync(oldTerminal, issuerId, succeeded: true);
        await issuer.DeactivateAsync();

        var newestTerminal = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        await runner.RedeemOnlyAsync(newestTerminal, issuerId);
        await runner.RepeatOutcomeAsync(newestTerminal, issuerId, succeeded: true);

        var consumed = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));
        await runner.RedeemOnlyAsync(consumed, issuerId);

        for (var index = 3; index < MaximumRememberedDelegations; index++)
        {
            await issuer.IssueAsync(
                runner.GetGrainId(),
                targetId,
                nameof(IDelegatedCapabilityTarget.EnterAsync));
        }

        await issuer.DeactivateAsync();

        await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(oldTerminal, issuerId, succeeded: true));
        await runner.RepeatOutcomeAsync(newestTerminal, issuerId, succeeded: true);
        await runner.RepeatOutcomeAsync(consumed, issuerId, succeeded: true);
        await runner.RepeatOutcomeAsync(consumed, issuerId, succeeded: true);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(consumed, issuerId, succeeded: false));

        var journal = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Equal(
            MaximumRememberedDelegations + 1,
            journal.Delta.Count(Is<CapabilityRequested>));
        Assert.Equal(3, journal.Delta.Count(Is<CapabilityCompleted>));
        Assert.DoesNotContain(journal.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(journal.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "a legitimate delegated target failure produces only one failed outcome")]
    public async Task LegitimateDelegatedTargetFailureProducesOnlyOneFailedOutcome()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-failure");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.FailAsync));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.InvokeFailureAsync(delegation, targetId));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);
        Assert.Single(callerOutgoing.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityCompleted>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "a legitimate target authorization failure is Failed and remains terminally consistent")]
    public async Task LegitimateTargetAuthorizationFailureIsFailedAndTerminallyConsistent()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-target-authorization-failure");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.RejectAsync));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.InvokeAuthorizationFailureAsync(delegation, targetId));
        await runner.RepeatOutcomeAsync(delegation, issuerId, succeeded: false);
        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => runner.RepeatOutcomeAsync(delegation, issuerId, succeeded: true));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityRejected>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityCompleted>);
    }

    [Fact(DisplayName = "outcome callback failure does not replace the semantic target exception")]
    public async Task OutcomeCallbackFailureDoesNotReplaceSemanticTargetException()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-outcome-callback-failure");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.RejectAsync));
        SimulationCluster.FailJournalWriteAfter(
            issuerId.ToGrainId(),
            completedWritesBeforeFailure: 1,
            "injected delegation outcome callback persistence failure");

        NeuronAuthorizationException failure;

        try
        {
            failure = await Assert.ThrowsAsync<NeuronAuthorizationException>(
                () => runner.InvokeAuthorizationFailureAsync(delegation, targetId));
        }
        finally
        {
            SimulationCluster.ClearJournalWriteFailure(issuerId.ToGrainId());
        }

        Assert.Equal("Expected delegated target authorization failure.", failure.Message);
        Assert.Contains(
            nameof(DelegatedCapabilityTarget.RejectAsync),
            failure.StackTrace,
            StringComparison.Ordinal);
        Assert.Contains(
            failure.Data.Values.Cast<object?>(),
            value => value?.ToString()?.Contains(
                "injected delegation outcome callback persistence failure",
                StringComparison.Ordinal) is true);
    }

    [Fact(DisplayName = "throwing semantic diagnostic data cannot mask the original callback failure path")]
    public async Task ThrowingSemanticDiagnosticDataCannotMaskOriginalCallbackFailurePath()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-throwing-diagnostic-data");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var delegation = await issuer.IssueAsync(
            runner.GetGrainId(),
            targetId,
            nameof(IDelegatedCapabilityTarget.ThrowDiagnosticDataAsync));
        SimulationCluster.FailJournalWriteAfter(
            issuerId.ToGrainId(),
            completedWritesBeforeFailure: 1,
            "injected delegation outcome callback persistence failure");

        ThrowingDiagnosticDataException failure;

        try
        {
            failure = await Assert.ThrowsAsync<ThrowingDiagnosticDataException>(
                () => runner.InvokeThrowingDataFailureAsync(delegation, targetId));
        }
        finally
        {
            SimulationCluster.ClearJournalWriteFailure(issuerId.ToGrainId());
        }

        Assert.Equal("Expected throwing diagnostic data semantic failure.", failure.Message);
        Assert.Contains(
            nameof(DelegatedCapabilityTarget.ThrowDiagnosticDataAsync),
            failure.StackTrace,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "delegation rejects an overloaded method name because it is not an exact operation")]
    public async Task DelegationRejectsAnOverloadedMethodName()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-overload");
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => issuer.IssueOverloadedAsync(runner.GetGrainId(), targetId));
    }

    [Fact(DisplayName = "a delegated call preserves its committed causal request and completes exactly once")]
    public async Task DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("delegation-valid");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var issuerId = NeuronId.For<DelegationIssuer>(owner, "issuer");
        var targetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "target");
        var issuer = SimulationCluster.Grains.GetGrain<IDelegationIssuer>(issuerId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/runner"));
        var runnerId = runner.GetGrainId();
        var observer = new DelegatedTargetJournalObserver();
        var observerReference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(observer);
        DelegatedTargetCommitObservations.Expect(owner);

        await simulation.WatchAsync(
            JournalKind.Incoming,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0,
            observerReference);

        await simulation.SendAsync(
            nameof(BeginDelegatedCall),
            nameof(DelegationIssuer),
            "issuer",
            NoValues);
        var delegation = DelegationIssuance.Take(owner);

        await issuer.DeactivateAsync();

        Assert.Equal(17, await runner.InvokeAsync(delegation, targetId));

        var callerIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);
        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegationIssuer),
            "issuer",
            afterSequence: 0);
        var targetIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0);
        var targetOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(DelegatedCapabilityTarget),
            "target",
            afterSequence: 0);
        var requested = Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);
        var received = Assert.Single(targetIncoming.Delta, delivery => delivery.SynapseId == requested.SynapseId);
        var observed = Assert.Single(targetOutgoing.Delta, Is<DelegatedCapabilityObserved>);
        var completed = Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
        var stimulus = Assert.Single(callerIncoming.Delta, Is<BeginDelegatedCall>);

        Assert.Equal(requested.SynapseId, received.SynapseId);
        Assert.Equal(requested.CorrelationId, received.CorrelationId);
        Assert.Equal(requested.CausationId, received.CausationId);
        Assert.NotNull(requested.CausationId);
        Assert.Equal(stimulus.SynapseId, requested.CausationId);
        Assert.Equal(stimulus.CorrelationId, requested.CorrelationId);
        Assert.Equal(requested.SynapseId, observed.CausationId);
        Assert.Equal(requested.SynapseId, completed.CausationId);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityRejected>);
    }

    private static bool Is<TSynapse>(SynapseDelivery delivery)
        where TSynapse : Synapse
        => delivery.Synapse is TSynapse;

    private sealed class DelegatedTargetJournalObserver : IJournalObserver
    {
        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            foreach (var delivery in read.Delta.Where(Is<CapabilityRequested>))
            {
                DelegatedTargetCommitObservations.Record(delivery.SynapseId);
            }

            return Task.CompletedTask;
        }
    }
}

internal static class DelegatedTargetCommitObservations
{
    private static readonly ConcurrentDictionary<SynapseId, byte> Committed = new();
    private static readonly ConcurrentDictionary<OwnerId, byte> Expected = new();

    internal static void Expect(OwnerId owner) => Expected.TryAdd(owner, 0);

    internal static bool IsExpected(OwnerId owner) => Expected.ContainsKey(owner);

    internal static bool Contains(SynapseId request) => Committed.ContainsKey(request);

    internal static void Record(SynapseId request) => Committed.TryAdd(request, 0);
}

internal static class DelegatedTargetStorageObservations
{
    private static readonly ConcurrentDictionary<OwnerId, Expectation> Expected = new();

    internal static void Expect(OwnerId owner, GrainId target, long completedWrites)
        => Expected[owner] = new(target, completedWrites);

    internal static void VerifyCompletedBeforeEntry(OwnerId owner)
    {
        if (!Expected.TryRemove(owner, out var expected))
        {
            return;
        }

        var actual = SimulationCluster.CompletedJournalWrites(expected.Target);

        if (actual != expected.CompletedWrites + 1)
        {
            throw new InvalidOperationException(
                "Exactly one target journal write must complete before semantic method entry.");
        }
    }

    private sealed record Expectation(GrainId Target, long CompletedWrites);
}

internal static class DelegatedTargetEntryObservations
{
    private static readonly ConcurrentDictionary<OwnerId, int> Entries = new();

    internal static void Reset(OwnerId owner) => Entries[owner] = 0;

    internal static void ObserveAtEntry(OwnerId owner)
    {
        Entries.AddOrUpdate(owner, 1, static (_, count) => count + 1);
        DelegatedTargetStorageObservations.VerifyCompletedBeforeEntry(owner);
    }

    internal static int Count(OwnerId owner) => Entries.GetValueOrDefault(owner);
}

[GenerateSerializer]
[Alias("db.test.delegated-capability-observed")]
internal sealed record DelegatedCapabilityObserved : Synapse;

[GenerateSerializer]
[Alias("db.test.begin-delegated-call")]
internal sealed record BeginDelegatedCall : Synapse;

[Alias("db.test.delegation-issuer")]
[ClientEntryPoint]
internal interface IDelegationIssuer : INeuron
{
    [Alias("Issue")]
    Task<CapabilityDelegation> IssueAsync(
        GrainId delegateSource,
        NeuronId target,
        string method);

    [Alias("IssueOverloaded")]
    Task<CapabilityDelegation> IssueOverloadedAsync(
        GrainId delegateSource,
        NeuronId target);

    [Alias("Deactivate")]
    Task DeactivateAsync();
}

internal sealed class DelegationIssuer
    : Neuron,
      IDelegationIssuer,
      IHandle<BeginDelegatedCall>
{
    public async Task HandleAsync(
        BeginDelegatedCall synapse,
        CancellationToken cancellationToken)
    {
        var runner = GrainFactory.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{Id.Owner.Value}/runner"));
        var target = NeuronId.For<DelegatedCapabilityTarget>(Id.Owner, "target");
        var delegation = await DelegateCapabilityAsync(
            runner.GetGrainId(),
            target,
            typeof(IDelegatedCapabilityTarget),
            nameof(IDelegatedCapabilityTarget.EnterAsync));

        DelegationIssuance.Record(Id.Owner, delegation);
    }

    public Task<CapabilityDelegation> IssueAsync(
        GrainId delegateSource,
        NeuronId target,
        string method)
        => DelegateCapabilityAsync(
            delegateSource,
            target,
            typeof(IDelegatedCapabilityTarget),
            method);

    public Task<CapabilityDelegation> IssueOverloadedAsync(
        GrainId delegateSource,
        NeuronId target)
        => DelegateCapabilityAsync(
            delegateSource,
            target,
            typeof(IOverloadedDelegatedTarget),
            nameof(IOverloadedDelegatedTarget.InvokeAsync));

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }
}

internal static class DelegationIssuance
{
    private static readonly ConcurrentDictionary<OwnerId, CapabilityDelegation> Issued = new();

    internal static void Record(OwnerId owner, CapabilityDelegation delegation)
        => Issued[owner] = delegation;

    internal static CapabilityDelegation Take(OwnerId owner)
        => Issued.TryRemove(owner, out var delegation)
            ? delegation
            : throw new InvalidOperationException($"No delegation was issued for '{owner}'.");
}

[Alias("db.test.overloaded-delegated-target")]
internal interface IOverloadedDelegatedTarget : INeuron
{
    [Alias("InvokeWithoutArgument")]
    Task<int> InvokeAsync();

    [Alias("InvokeWithArgument")]
    Task<int> InvokeAsync(int value);
}

[Alias("db.test.delegated-runner")]
internal interface IDelegatedRunner : IGrainWithStringKey
{
    [Alias("Invoke")]
    Task<int> InvokeAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("InvokeFailure")]
    Task<int> InvokeFailureAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("InvokeAuthorizationFailure")]
    Task<int> InvokeAuthorizationFailureAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("InvokeThrowingDataFailure")]
    Task<int> InvokeThrowingDataFailureAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("InvokeAlternate")]
    Task<int> InvokeAlternateAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("InvokeForged")]
    Task<int> InvokeForgedAsync(SynapseDelivery delivery, NeuronId target);

    [Alias("InvokeBlocked")]
    Task<int> InvokeBlockedAsync(CapabilityDelegation delegation, NeuronId target);

    [Alias("RepeatOutcome")]
    Task RepeatOutcomeAsync(
        CapabilityDelegation delegation,
        NeuronId causalCaller,
        bool succeeded);

    [Alias("RedeemOnly")]
    Task RedeemOnlyAsync(
        CapabilityDelegation delegation,
        NeuronId causalCaller);
}

[Reentrant]
internal sealed class DelegatedRunner(IGrainFactory grains) : Grain, IDelegatedRunner
{
    public Task<int> InvokeAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId()).EnterAsync());

    public Task<int> InvokeFailureAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId()).FailAsync());

    public Task<int> InvokeAuthorizationFailureAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId()).RejectAsync());

    public Task<int> InvokeThrowingDataFailureAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId()).ThrowDiagnosticDataAsync());

    public Task<int> InvokeAlternateAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IAlternateDelegatedTarget>(target.ToGrainId()).EnterAsync());

    public async Task<int> InvokeForgedAsync(SynapseDelivery delivery, NeuronId target)
    {
        RequestContext.Set("db.capability-request", delivery);

        try
        {
            return await grains
                .GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId())
                .EnterAsync();
        }
        finally
        {
            RequestContext.Remove("db.capability-request");
        }
    }

    public Task<int> InvokeBlockedAsync(CapabilityDelegation delegation, NeuronId target)
        => DigitalBrainRuntime.InvokeAsync(
            delegation,
            () => grains.GetGrain<IDelegatedCapabilityTarget>(target.ToGrainId()).BlockAsync());

    public async Task RepeatOutcomeAsync(
        CapabilityDelegation delegation,
        NeuronId causalCaller,
        bool succeeded)
    {
        var authorityType = typeof(Neuron).Assembly.GetType(
            "DigitalBrain.Kernel.ICapabilityDelegationAuthority",
            throwOnError: true)!;
        var finish = authorityType.GetMethod("FinishAsync")
            ?? throw new InvalidOperationException("The Kernel delegation authority has no finish callback.");
        var authority = grains.GetGrain(
            causalCaller.ToGrainId(),
            GrainInterfaceType.Create("db.kernel.capability-delegation-authority"));
        var callback = finish.Invoke(authority, [delegation, succeeded]) as Task
            ?? throw new InvalidOperationException("The Kernel delegation finish callback did not return a Task.");

        await callback;
    }

    public async Task RedeemOnlyAsync(
        CapabilityDelegation delegation,
        NeuronId causalCaller)
    {
        var authorityType = typeof(Neuron).Assembly.GetType(
            "DigitalBrain.Kernel.ICapabilityDelegationAuthority",
            throwOnError: true)!;
        var redeem = authorityType.GetMethod("RedeemAsync")
            ?? throw new InvalidOperationException("The Kernel delegation authority has no redeem callback.");
        var authority = grains.GetGrain(
            causalCaller.ToGrainId(),
            GrainInterfaceType.Create("db.kernel.capability-delegation-authority"));
        var callback = redeem.Invoke(authority, [delegation]) as Task
            ?? throw new InvalidOperationException("The Kernel delegation redeem callback did not return a Task.");

        await callback;
    }
}

[Alias("db.test.delegated-capability-target")]
internal interface IDelegatedCapabilityTarget : INeuron
{
    [Alias("Enter")]
    Task<int> EnterAsync();

    [Alias("Fail")]
    Task<int> FailAsync();

    [Alias("Reject")]
    Task<int> RejectAsync();

    [Alias("ThrowDiagnosticData")]
    Task<int> ThrowDiagnosticDataAsync();

    [Alias("Block")]
    Task<int> BlockAsync();
}

[Alias("db.test.alternate-delegated-target")]
internal interface IAlternateDelegatedTarget : INeuron
{
    [Alias("Enter")]
    Task<int> EnterAsync();
}

internal sealed class DelegatedCapabilityTarget
    : Neuron,
      IDelegatedCapabilityTarget,
      IAlternateDelegatedTarget,
      IEmit<DelegatedCapabilityObserved>
{
    public async Task<int> EnterAsync()
    {
        DelegatedTargetEntryObservations.ObserveAtEntry(Id.Owner);

        var incoming = await ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);
        var request = incoming.Delta.Single(delivery => delivery.Synapse is CapabilityRequested);

        if (DelegatedTargetCommitObservations.IsExpected(Id.Owner)
            && !DelegatedTargetCommitObservations.Contains(request.SynapseId))
        {
            throw new InvalidOperationException(
                "The target request was not durably observed before semantic method entry.");
        }

        var caller = GrainFactory.GetGrain<INeuron>(request.Caller.ToGrainId());
        var callerOutgoing = await caller.ReadJournalAsync(JournalKind.Outgoing, afterSequence: 0);

        if (!callerOutgoing.Delta.Any(delivery => delivery.SynapseId == request.SynapseId))
        {
            throw new InvalidOperationException(
                "The causal caller's request was not durably observable before target entry.");
        }

        await EmitAsync(new DelegatedCapabilityObserved());

        return 17;
    }

    public Task<int> FailAsync()
        => throw new InvalidOperationException("Expected delegated target failure.");

    public Task<int> RejectAsync()
        => throw new NeuronAuthorizationException("Expected delegated target authorization failure.");

    public Task<int> ThrowDiagnosticDataAsync()
        => throw new ThrowingDiagnosticDataException();

    public async Task<int> BlockAsync()
    {
        var incoming = await ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);

        if (!incoming.Delta.Any(delivery => delivery.Synapse is CapabilityRequested))
        {
            throw new InvalidOperationException(
                "The target request was not committed before semantic method entry.");
        }

        await DelegatedInvocationGate.EnterAsync(Id.Owner);

        return 23;
    }
}

[GenerateSerializer]
[Alias("db.test.throwing-diagnostic-data-error")]
internal sealed class ThrowingDiagnosticDataException : Exception
{
    private static readonly IDictionary ThrowingData =
        new ReadOnlyDictionary<object, object>(new Dictionary<object, object>());

    public ThrowingDiagnosticDataException()
        : this("Expected throwing diagnostic data semantic failure.")
    {
    }

    public ThrowingDiagnosticDataException(string message)
        : base(message)
    {
    }

    public ThrowingDiagnosticDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public override IDictionary Data => ThrowingData;
}

internal static class DelegatedInvocationGate
{
    private static readonly ConcurrentDictionary<OwnerId, Gate> Gates = new();

    internal static void Reset(OwnerId owner) => Gates[owner] = new Gate();

    internal static Task Entered(OwnerId owner) => For(owner).Entered.Task;

    internal static int EntryCount(OwnerId owner) => For(owner).Entries;

    internal static async Task EnterAsync(OwnerId owner)
    {
        var gate = For(owner);
        Interlocked.Increment(ref gate.Entries);
        gate.Entered.TrySetResult();
        await gate.Released.Task;
    }

    internal static void Release(OwnerId owner) => For(owner).Released.TrySetResult();

    private static Gate For(OwnerId owner)
        => Gates.TryGetValue(owner, out var gate)
            ? gate
            : throw new InvalidOperationException($"No invocation gate exists for '{owner}'.");

    private sealed class Gate
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Entries;
    }
}
