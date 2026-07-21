using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class CapabilityCausalContinuationContracts
{
    private const int MaximumCapturedCapabilityCauses = 32;

    [Fact(DisplayName = "deferred delegation and reply descend from the captured committed capability request")]
    public async Task DeferredWorkRetainsTheCapturedCapabilityCausation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var semanticTargetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "semantic-target");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/deferred-runner"));

        var causation = await driver.CaptureAsync(continuationId);
        var delegation = await driver.DelegateAsync(
            continuationId,
            causation,
            runner.GetGrainId(),
            semanticTargetId);

        Assert.Equal(17, await runner.InvokeAsync(delegation, semanticTargetId));

        await InfrastructureRunner(owner).CompleteAsync(continuationId, causation);

        var continuationIncoming = await driver.ReadAsync(continuationId, JournalKind.Incoming);
        var continuationOutgoing = await driver.ReadAsync(continuationId, JournalKind.Outgoing);
        var driverIncoming = await ReadUntilAsync(
            driver,
            driverId,
            delivery => delivery.Synapse is DeferredCausalReply);
        var captured = Assert.Single(
            continuationIncoming.Delta,
            delivery => delivery.Synapse is CapabilityRequested requested
                && requested.Method == nameof(ICausalContinuationTarget.CaptureAsync));
        var delegated = Assert.Single(
            continuationOutgoing.Delta,
            delivery => delivery.Synapse is CapabilityRequested requested
                && requested.Method == nameof(IDelegatedCapabilityTarget.EnterAsync));
        var replied = Assert.Single(driverIncoming.Delta, delivery => delivery.Synapse is DeferredCausalReply);

        Assert.Equal(captured.SynapseId, causation);
        Assert.Equal(captured.SynapseId, delegated.CausationId);
        Assert.Equal(captured.CorrelationId, delegated.CorrelationId);
        Assert.Equal(captured.SynapseId, replied.CausationId);
        Assert.Equal(captured.CorrelationId, replied.CorrelationId);
    }

    [Fact(DisplayName = "capability causation can be captured only during a committed capability request")]
    public async Task CaptureRequiresACommittedIncomingCapabilityRequest()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-capture-scope");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var target = SimulationCluster.Grains.GetGrain<ICausalContinuationTarget>(continuationId.ToGrainId());

        await Assert.ThrowsAsync<InvalidOperationException>(() => target.CaptureAsync(
            NeuronId.For<CausalContinuationDriver>(owner, "driver")));
    }

    [Fact(DisplayName = "capability causation rejects an uncommitted delivered request fact")]
    public async Task CaptureRejectsAnUncommittedDeliveredCapabilityRequest()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-capture-uncommitted");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        CausalContinuationCaptureObservations.Reset(continuationId);

        await driver.SendUncommittedCaptureAsync(continuationId);

        for (var attempt = 0; attempt < 100 && !CausalContinuationCaptureObservations.TryRead(
                 continuationId,
                 out _); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        Assert.True(CausalContinuationCaptureObservations.TryRead(continuationId, out var rejected));
        Assert.True(rejected);
    }

    [Fact(DisplayName = "deferred causal continuation rejects unknown and foreign incoming request ids")]
    public async Task DeferredContinuationRejectsUnknownAndForeignCausation()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-fence");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var firstId = NeuronId.For<CausalContinuationTarget>(owner, "first");
        var secondId = NeuronId.For<CausalContinuationTarget>(owner, "second");
        var semanticTargetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "semantic-target");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/deferred-runner"));
        var foreign = await driver.CaptureAsync(secondId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.DelegateAsync(
            firstId,
            SynapseId.New(),
            runner.GetGrainId(),
            semanticTargetId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.DelegateAsync(
            firstId,
            foreign,
            runner.GetGrainId(),
            semanticTargetId));
        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => driver.CaptureExpectingAsync(
            firstId,
            NeuronId.For<CausalContinuationDriver>(owner, "wrong-driver")));
    }

    [Fact(DisplayName = "captured capability causation remains usable after retained-journal compaction")]
    public async Task CapturedCausationSurvivesRetainedJournalCompaction()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-compaction");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var semanticTargetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "semantic-target");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/deferred-runner"));
        var causation = await driver.CaptureAsync(continuationId);

        for (var index = 0; index < 513; index++)
        {
            await driver.TouchAsync(continuationId);
        }

        var compacted = await driver.ReadAsync(continuationId, JournalKind.Incoming);
        Assert.NotNull(compacted.ResetSnapshot);

        await SimulationCluster.RestartHostOfAsync(continuationId);

        var delegation = await driver.DelegateAsync(
            continuationId,
            causation,
            runner.GetGrainId(),
            semanticTargetId);

        Assert.Equal(17, await runner.InvokeAsync(delegation, semanticTargetId));
    }

    [Fact(DisplayName = "capture rollback and duplicate capture do not consume extra bounded capacity")]
    public async Task FailedAndDuplicateCaptureDoNotLeakCapacity()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-capture-rollback");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.CaptureThenThrowAsync(continuationId));

        var duplicate = await driver.CaptureTwiceAsync(continuationId);
        Assert.Equal(duplicate[0], duplicate[1]);

        for (var index = 1; index < MaximumCapturedCapabilityCauses; index++)
        {
            _ = await driver.CaptureAsync(continuationId);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.CaptureAsync(continuationId));
    }

    [Fact(DisplayName = "captured causation backpressures without eviction until terminal reply releases one slot")]
    public async Task CapturedCausationBackpressuresUntilTerminalReply()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-capacity");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var semanticTargetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "semantic-target");
        var retainedTargetId = NeuronId.For<DelegatedCapabilityTarget>(owner, "retained-target");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var runner = SimulationCluster.Grains.GetGrain<IDelegatedRunner>(
            IdSpan.Create($"{owner.Value}/deferred-runner"));
        var captured = new List<SynapseId>();

        for (var index = 0; index < MaximumCapturedCapabilityCauses; index++)
        {
            captured.Add(await driver.CaptureAsync(continuationId));
        }

        await SimulationCluster.RestartHostOfAsync(continuationId);

        var delegation = await driver.DelegateAsync(
            continuationId,
            captured[0],
            runner.GetGrainId(),
            semanticTargetId);
        Assert.Equal(17, await runner.InvokeAsync(delegation, semanticTargetId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.CaptureAsync(continuationId));

        await InfrastructureRunner(owner).CompleteAsync(continuationId, captured[0]);

        _ = await driver.CaptureAsync(continuationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InfrastructureRunner(owner).CompleteAsync(continuationId, captured[0]));

        var retainedDelegation = await driver.DelegateAsync(
            continuationId,
            captured[^1],
            runner.GetGrainId(),
            retainedTargetId);
        Assert.Equal(17, await runner.InvokeAsync(retainedDelegation, retainedTargetId));
    }

    [Fact(DisplayName = "a failed deferred reply rolls back capture release and its staged outbox fact")]
    public async Task FailedDeferredReplyRollsBackReleaseAndFact()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-reply-rollback");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var causation = await driver.CaptureAsync(continuationId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.ReplyThenThrowAsync(continuationId, causation));
        await driver.ReplyAsync(continuationId, causation);

        var received = await ReadUntilAsync(
            driver,
            driverId,
            delivery => delivery.Synapse is DeferredCausalReply);

        Assert.Single(received.Delta, delivery => delivery.Synapse is DeferredCausalReply);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            driver.ReplyAsync(continuationId, causation));
    }

    [Fact(DisplayName = "a failed deferred reply commit cannot leak its outgoing fact into a later commit")]
    public async Task FailedDeferredReplyCommitRollsBackOutgoingJournal()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("causal-continuation-reply-write-rollback");
        var driverId = NeuronId.For<CausalContinuationDriver>(owner, "driver");
        var continuationId = NeuronId.For<CausalContinuationTarget>(owner, "continuation");
        var driver = SimulationCluster.Grains.GetGrain<ICausalContinuationDriver>(driverId.ToGrainId());
        var causation = await driver.CaptureAsync(continuationId);
        var targetGrain = continuationId.ToGrainId();

        SimulationCluster.FailJournalWriteAfter(
            targetGrain,
            completedWritesBeforeFailure: 0,
            "Expected deferred-reply commit failure.");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InfrastructureRunner(owner).CompleteAsync(continuationId, causation));
        }
        finally
        {
            SimulationCluster.ClearJournalWriteFailure(targetGrain);
        }

        await driver.TouchAsync(continuationId);

        var afterUnrelatedCommit = await driver.ReadAsync(continuationId, JournalKind.Outgoing);
        Assert.DoesNotContain(
            afterUnrelatedCommit.Delta,
            delivery => delivery.Synapse is DeferredCausalReply);

        await InfrastructureRunner(owner).CompleteAsync(continuationId, causation);

        var received = await ReadUntilAsync(
            driver,
            driverId,
            delivery => delivery.Synapse is DeferredCausalReply);
        var outgoing = await driver.ReadAsync(continuationId, JournalKind.Outgoing);

        Assert.Single(outgoing.Delta, delivery => delivery.Synapse is DeferredCausalReply);
        Assert.Single(received.Delta, delivery => delivery.Synapse is DeferredCausalReply);
    }

    private static async Task<JournalRead> ReadUntilAsync(
        ICausalContinuationDriver driver,
        NeuronId neuron,
        Func<SynapseDelivery, bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var read = await driver.ReadAsync(neuron, JournalKind.Incoming);

            if (read.Delta.Any(predicate))
            {
                return read;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Neuron '{neuron}' did not receive the expected deferred reply.");
    }

    private static ICausalContinuationInfrastructureRunner InfrastructureRunner(OwnerId owner)
        => SimulationCluster.Grains.GetGrain<ICausalContinuationInfrastructureRunner>(
            IdSpan.Create($"{owner.Value}/causal-continuation-infrastructure-runner"));
}

[GenerateSerializer]
[Alias("db.test.deferred-causal-reply")]
internal sealed record DeferredCausalReply : Synapse;

[Alias("db.test.causal-continuation-driver")]
[ClientEntryPoint]
internal interface ICausalContinuationDriver : INeuron
{
    [Alias("Capture")]
    Task<SynapseId> CaptureAsync(NeuronId target);

    [Alias("CaptureExpecting")]
    Task<SynapseId> CaptureExpectingAsync(NeuronId target, NeuronId expectedCaller);

    [Alias("Delegate")]
    Task<CapabilityDelegation> DelegateAsync(
        NeuronId target,
        SynapseId causation,
        GrainId delegateSource,
        NeuronId semanticTarget);

    [Alias("Reply")]
    Task ReplyAsync(NeuronId target, SynapseId causation);

    [Alias("Touch")]
    Task TouchAsync(NeuronId target);

    [Alias("CaptureTwice")]
    Task<SynapseId[]> CaptureTwiceAsync(NeuronId target);

    [Alias("CaptureThenThrow")]
    Task<SynapseId> CaptureThenThrowAsync(NeuronId target);

    [Alias("ReplyThenThrow")]
    Task ReplyThenThrowAsync(NeuronId target, SynapseId causation);

    [Alias("SendUncommittedCapture")]
    Task SendUncommittedCaptureAsync(NeuronId target);

    [Alias("Read")]
    Task<JournalRead> ReadAsync(NeuronId target, JournalKind kind);
}

internal sealed class CausalContinuationDriver
    : Neuron,
      ICausalContinuationDriver,
      IHandle<DeferredCausalReply>,
      IEmit<CapabilityRequested>
{
    public Task<SynapseId> CaptureAsync(NeuronId target)
        => Target(target).CaptureAsync(Id);

    public Task<SynapseId> CaptureExpectingAsync(NeuronId target, NeuronId expectedCaller)
        => Target(target).CaptureAsync(expectedCaller);

    public Task<CapabilityDelegation> DelegateAsync(
        NeuronId target,
        SynapseId causation,
        GrainId delegateSource,
        NeuronId semanticTarget)
        => Target(target).DelegateAsync(causation, delegateSource, semanticTarget);

    public Task ReplyAsync(NeuronId target, SynapseId causation)
        => Target(target).ReplyAsync(causation);

    public Task TouchAsync(NeuronId target) => Target(target).TouchAsync();

    public Task<SynapseId[]> CaptureTwiceAsync(NeuronId target)
        => Target(target).CaptureTwiceAsync(Id);

    public Task<SynapseId> CaptureThenThrowAsync(NeuronId target)
        => Target(target).CaptureThenThrowAsync(Id);

    public Task ReplyThenThrowAsync(NeuronId target, SynapseId causation)
        => Target(target).ReplyThenThrowAsync(causation);

    public Task SendUncommittedCaptureAsync(NeuronId target)
        => SendAsync(
            target,
            new CapabilityRequested(
                typeof(ICausalContinuationTarget).FullName!,
                nameof(ICausalContinuationTarget.CaptureAsync),
                target));

    public Task<JournalRead> ReadAsync(NeuronId target, JournalKind kind)
        => target == Id
            ? ReadJournalAsync(kind, afterSequence: 0)
            : GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadJournalAsync(kind, afterSequence: 0);

    public Task HandleAsync(DeferredCausalReply synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private ICausalContinuationTarget Target(NeuronId target)
        => GrainFactory.GetGrain<ICausalContinuationTarget>(target.ToGrainId());
}

[Alias("db.test.causal-continuation-target")]
[ClientEntryPoint]
internal interface ICausalContinuationTarget : INeuron
{
    [Alias("Capture")]
    Task<SynapseId> CaptureAsync(NeuronId expectedCaller);

    [Alias("Delegate")]
    Task<CapabilityDelegation> DelegateAsync(
        SynapseId causation,
        GrainId delegateSource,
        NeuronId semanticTarget);

    [Alias("Reply")]
    Task ReplyAsync(SynapseId causation);

    [Alias("Touch")]
    Task TouchAsync();

    [Alias("CaptureTwice")]
    Task<SynapseId[]> CaptureTwiceAsync(NeuronId expectedCaller);

    [Alias("CaptureThenThrow")]
    Task<SynapseId> CaptureThenThrowAsync(NeuronId expectedCaller);

    [Alias("ReplyThenThrow")]
    Task ReplyThenThrowAsync(SynapseId causation);
}

internal sealed class CausalContinuationTarget
    : Neuron,
      ICausalContinuationTarget,
      ICausalContinuationOwnerCallback,
      IHandle<CapabilityRequested>
{
    public Task<SynapseId> CaptureAsync(NeuronId expectedCaller)
        => Task.FromResult(CaptureCapabilityCausation(expectedCaller));

    public Task<CapabilityDelegation> DelegateAsync(
        SynapseId causation,
        GrainId delegateSource,
        NeuronId semanticTarget)
        => DelegateCapabilityAsync(
            causation,
            delegateSource,
            semanticTarget,
            typeof(IDelegatedCapabilityTarget),
            nameof(IDelegatedCapabilityTarget.EnterAsync));

    public Task ReplyAsync(SynapseId causation)
        => ReplyAsync(causation, new DeferredCausalReply());

    public Task CompleteDeferredAsync(SynapseId causation)
        => ReplyAsync(causation, new DeferredCausalReply());

    public Task TouchAsync() => Task.CompletedTask;

    public Task<SynapseId[]> CaptureTwiceAsync(NeuronId expectedCaller)
    {
        var first = CaptureCapabilityCausation(expectedCaller);
        var second = CaptureCapabilityCausation(expectedCaller);

        return Task.FromResult(new[] { first, second });
    }

    public Task<SynapseId> CaptureThenThrowAsync(NeuronId expectedCaller)
    {
        _ = CaptureCapabilityCausation(expectedCaller);

        throw new InvalidOperationException("Expected failure after capability causation capture.");
    }

    public async Task ReplyThenThrowAsync(SynapseId causation)
    {
        await ReplyAsync(causation, new DeferredCausalReply());

        throw new InvalidOperationException("Expected failure after deferred reply staging.");
    }

    public Task HandleAsync(CapabilityRequested synapse, CancellationToken cancellationToken)
    {
        try
        {
            _ = CaptureCapabilityCausation(
                NeuronId.For<CausalContinuationDriver>(Id.Owner, "driver"));
            CausalContinuationCaptureObservations.Record(Id, rejected: false);
        }
        catch (InvalidOperationException)
        {
            CausalContinuationCaptureObservations.Record(Id, rejected: true);
        }

        return Task.CompletedTask;
    }
}

[Alias("db.test.causal-continuation-infrastructure-runner")]
internal interface ICausalContinuationInfrastructureRunner : IGrainWithStringKey
{
    [Alias("Complete")]
    Task CompleteAsync(NeuronId target, SynapseId causation);
}

internal sealed class CausalContinuationInfrastructureRunner(IGrainFactory grains)
    : Grain,
      ICausalContinuationInfrastructureRunner
{
    public Task CompleteAsync(NeuronId target, SynapseId causation)
        => grains.GetGrain<ICausalContinuationOwnerCallback>(target.ToGrainId())
            .CompleteDeferredAsync(causation);
}

internal static class CausalContinuationCaptureObservations
{
    private static readonly ConcurrentDictionary<NeuronId, bool> Observed = new();

    internal static void Reset(NeuronId target) => Observed.TryRemove(target, out _);

    internal static void Record(NeuronId target, bool rejected) => Observed[target] = rejected;

    internal static bool TryRead(NeuronId target, out bool rejected)
        => Observed.TryGetValue(target, out rejected);
}
