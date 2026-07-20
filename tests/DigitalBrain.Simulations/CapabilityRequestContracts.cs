using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class CapabilityRequestContracts
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "a capability request preserves exact causal lineage across both neurons")]
    public async Task CapabilityRequestPreservesExactCausalLineageAcrossBothNeurons()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("capability-lineage");

        await simulation.SendAsync("Ping", nameof(CapabilityCaller), "caller", NoValues);

        var callerIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(CapabilityCaller),
            "caller",
            afterSequence: 0);
        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(CapabilityCaller),
            "caller",
            afterSequence: 0);
        var targetIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Echo),
            "probe",
            afterSequence: 0);
        var targetOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(Echo),
            "probe",
            afterSequence: 0);

        var stimulus = Assert.Single(callerIncoming.Delta);
        var requested = Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);
        var received = Assert.Single(
            targetIncoming.Delta,
            delivery => delivery.SynapseId == requested.SynapseId);
        var observed = Assert.Single(targetOutgoing.Delta, Is<CapabilityObserved>);
        var completed = Assert.Single(callerOutgoing.Delta, Is<CapabilityCompleted>);
        var request = Assert.IsType<CapabilityRequested>(requested.Synapse);
        var outcome = Assert.IsType<CapabilityCompleted>(completed.Synapse);

        Assert.Equal(NeuronId.For<Echo>(simulation.Owner, "probe"), request.Target);
        Assert.Equal(typeof(IEchoProbe).FullName, request.Contract);
        Assert.Equal(nameof(IEchoProbe.PokeAsync), request.Method);
        Assert.Equal(requested.SynapseId, received.SynapseId);
        Assert.Equal(stimulus.SynapseId, requested.CausationId);
        Assert.Equal(requested.SynapseId, observed.CausationId);
        Assert.Equal(requested.SynapseId, completed.CausationId);
        Assert.Equal(requested.SynapseId, outcome.Request);
        Assert.Equal(stimulus.CorrelationId, requested.CorrelationId);
        Assert.Equal(requested.CorrelationId, received.CorrelationId);
        Assert.Equal(requested.CorrelationId, observed.CorrelationId);
        Assert.Equal(requested.CorrelationId, completed.CorrelationId);
    }

    [Fact(DisplayName = "caller observers see the durable request before the target method runs")]
    public async Task CallerObserversSeeDurableRequestBeforeTargetMethodRuns()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("capability-before-invocation");

        var observer = new CapabilityRequestObserver();
        var reference = SimulationCluster.Grains.CreateObjectReference<IJournalObserver>(observer);

        await simulation.WatchAsync(
            JournalKind.Outgoing,
            nameof(TimingCapabilityCaller),
            "caller",
            afterSequence: 0,
            reference);

        await simulation.SendAsync(
            "Ping",
            nameof(TimingCapabilityCaller),
            "caller",
            NoValues);

        var outgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(TimingCapabilityCaller),
            "caller",
            afterSequence: 0);
        var requested = Assert.Single(outgoing.Delta, Is<CapabilityRequested>);

        Assert.True(CapabilityRequestObservations.Contains(requested.SynapseId));
    }

    [Fact(DisplayName = "an owner-rejected request is visible at the target and rejected at the caller")]
    public async Task OwnerRejectedRequestIsVisibleAtTargetAndRejectedAtCaller()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("capability-rejection-lineage");

        await simulation.SendAsync(
            "RejectCapability",
            nameof(RejectedCapabilityCaller),
            "caller",
            NoValues);

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(RejectedCapabilityCaller),
            "caller",
            afterSequence: 0);
        var targetIncoming = await Simulation.ReadJournalOfOwnerAsync(
            JournalKind.Incoming,
            owner: "foreign",
            nameof(Echo),
            "probe",
            afterSequence: 0);

        var requested = Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);
        var received = Assert.Single(
            targetIncoming.Delta,
            delivery => delivery.SynapseId == requested.SynapseId);
        var rejected = Assert.Single(callerOutgoing.Delta, Is<CapabilityRejected>);
        var outcome = Assert.IsType<CapabilityRejected>(rejected.Synapse);

        Assert.Equal(requested.SynapseId, received.SynapseId);
        Assert.Equal(requested.CorrelationId, received.CorrelationId);
        Assert.Equal(requested.SynapseId, rejected.CausationId);
        Assert.Equal(requested.SynapseId, outcome.Request);
    }

    [Fact(DisplayName = "effects committed before an unhandled capability failure are still delivered")]
    public async Task EffectsCommittedBeforeUnhandledCapabilityFailureAreStillDelivered()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("capability-failure-boundary");

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => simulation.SendAsync(
            "UnhandledFailCapability",
            nameof(UnhandledFailingCapabilityCaller),
            "caller",
            NoValues));

        Assert.Equal(
            1,
            await simulation.SettleAsync(
                JournalKind.Incoming,
                nameof(CapabilityBoundaryRecorder),
                "target"));

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(UnhandledFailingCapabilityCaller),
            "caller",
            afterSequence: 0);
        var callerIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(UnhandledFailingCapabilityCaller),
            "caller",
            afterSequence: 0);

        Assert.Collection(
            callerOutgoing.Delta,
            delivery => Assert.IsType<BeforeCapabilityRequest>(delivery.Synapse),
            delivery => Assert.IsType<CapabilityRequested>(delivery.Synapse),
            delivery => Assert.IsType<CapabilityFailed>(delivery.Synapse));
        Assert.Equal([1L, 2L, 3L], callerOutgoing.Delta.Select(delivery => delivery.Sequence));

        var requested = callerOutgoing.Delta[1];
        var failed = callerOutgoing.Delta[2];
        var outcome = Assert.IsType<CapabilityFailed>(failed.Synapse);
        var stimulus = Assert.Single(callerIncoming.Delta);

        Assert.Equal(stimulus.SynapseId, requested.CausationId);
        Assert.Equal(requested.SynapseId, failed.CausationId);
        Assert.Equal(requested.SynapseId, outcome.Request);
        Assert.Equal(requested.CorrelationId, failed.CorrelationId);
    }

    [Fact(DisplayName = "an authorization-shaped target failure is failed rather than rejected")]
    public async Task AuthorizationShapedTargetFailureIsFailedRatherThanRejected()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("capability-authorization-shaped-failure");

        await simulation.SendAsync(
            "AuthorizationShapedFailure",
            nameof(AuthorizationShapedFailureCaller),
            "caller",
            NoValues);

        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(AuthorizationShapedFailureCaller),
            "caller",
            afterSequence: 0);

        Assert.Single(callerOutgoing.Delta, Is<CapabilityFailed>);
        Assert.DoesNotContain(callerOutgoing.Delta, Is<CapabilityRejected>);
    }

    private static bool Is<TSynapse>(SynapseDelivery delivery)
        where TSynapse : Synapse
        => delivery.Synapse is TSynapse;

    private sealed class CapabilityRequestObserver : IJournalObserver
    {
        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            foreach (var delivery in read.Delta.Where(Is<CapabilityRequested>))
            {
                CapabilityRequestObservations.Record(delivery.SynapseId);
            }

            return Task.CompletedTask;
        }
    }
}

internal static class CapabilityRequestObservations
{
    private static readonly ConcurrentDictionary<SynapseId, byte> Observed = new();

    internal static bool Contains(SynapseId request) => Observed.ContainsKey(request);

    internal static void Record(SynapseId request) => Observed.TryAdd(request, 0);
}
