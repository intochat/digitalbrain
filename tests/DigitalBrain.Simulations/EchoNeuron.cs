using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.ping")]
internal sealed record Ping : Synapse;

[GenerateSerializer]
[Alias("db.test.capability-observed")]
internal sealed record CapabilityObserved : Synapse;

[GenerateSerializer]
[Alias("db.test.fail-capability")]
internal sealed record FailCapability : Synapse;

[GenerateSerializer]
[Alias("db.test.reject-capability")]
internal sealed record RejectCapability : Synapse;

[GenerateSerializer]
[Alias("db.test.unhandled-fail-capability")]
internal sealed record UnhandledFailCapability : Synapse;

[GenerateSerializer]
[Alias("db.test.before-capability-request")]
internal sealed record BeforeCapabilityRequest : Synapse;

[GenerateSerializer]
[Alias("db.test.authorization-shaped-failure")]
internal sealed record AuthorizationShapedFailure : Synapse;

[Alias("db.test.echo-probe")]
internal partial interface IEchoProbe : INeuron
{
    [Alias("Poke")]
    Task PokeAsync();
}

[Alias("db.test.timing-probe")]
internal partial interface ITimingProbe : INeuron
{
    [Alias("Poke")]
    Task PokeAsync();
}

internal sealed class Echo : Neuron, IHandle<Ping>, IEmit<CapabilityObserved>, IEchoProbe
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task PokeAsync()
    {
        var targetJournal = await ReadJournal(JournalKind.Incoming, afterSequence: 0);

        if (!ContainsRequest(targetJournal))
        {
            throw new InvalidOperationException(
                "The capability request must be committed by the target before its method executes.");
        }

        await EmitAsync(new CapabilityObserved());
    }

    private static bool ContainsRequest(JournalRead journal)
        => journal.Delta.Any(delivery => delivery.Synapse is CapabilityRequested);
}

internal sealed class CapabilityCaller : Neuron, IHandle<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => GrainFactory.GetGrain<IEchoProbe>(NeuronId.For<Echo>(Id.Owner, "probe").ToGrainId()).PokeAsync();
}

internal sealed class TimingCapabilityCaller : Neuron, IHandle<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => GrainFactory
            .GetGrain<ITimingProbe>(NeuronId.For<TimingProbe>(Id.Owner, "probe").ToGrainId())
            .PokeAsync();
}

internal sealed class TimingProbe : Neuron, ITimingProbe
{
    public async Task PokeAsync()
    {
        var targetJournal = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        var requested = AssertSingleRequest(targetJournal);

        if (!CapabilityRequestObservations.Contains(requested.SynapseId))
        {
            throw new InvalidOperationException(
                "The caller's committed request was not observable before the target method executed.");
        }
    }

    private static SynapseDelivery AssertSingleRequest(JournalRead journal)
        => journal.Delta.Single(delivery => delivery.Synapse is CapabilityRequested);
}

[Alias("db.test.failing-probe")]
internal partial interface IFailingProbe : INeuron
{
    [Alias("Fail")]
    Task FailAsync();

    [Alias("FailAuthorization")]
    Task FailAuthorizationAsync();
}

internal sealed class FailingProbe : Neuron, IFailingProbe
{
    public Task FailAsync()
        => throw new InvalidOperationException("This private failure must not enter a generic capability fact.");

    public Task FailAuthorizationAsync()
        => throw new NeuronAuthorizationException(
            "Target implementation failures must not be classified as owner-bound rejection.");
}

internal sealed class FailingCapabilityCaller : Neuron, IHandle<FailCapability>
{
    public async Task HandleAsync(FailCapability synapse, CancellationToken cancellationToken)
    {
        try
        {
            await GrainFactory
                .GetGrain<IFailingProbe>(NeuronId.For<FailingProbe>(Id.Owner, "probe").ToGrainId())
                .FailAsync();
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed class RejectedCapabilityCaller : Neuron, IHandle<RejectCapability>
{
    public async Task HandleAsync(RejectCapability synapse, CancellationToken cancellationToken)
    {
        var foreign = new NeuronId(
            NeuronId.GrainTypeNameOf(typeof(Echo)),
            new OwnerId("foreign"),
            "probe");

        try
        {
            await GrainFactory.GetGrain<IEchoProbe>(foreign.ToGrainId()).PokeAsync();
        }
        catch (NeuronAuthorizationException)
        {
        }
    }
}

internal sealed class UnhandledFailingCapabilityCaller
    : Neuron,
      IHandle<UnhandledFailCapability>,
      IEmit<BeforeCapabilityRequest>
{
    public async Task HandleAsync(
        UnhandledFailCapability synapse,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            NeuronId.For<CapabilityBoundaryRecorder>(Id.Owner, "target"),
            new BeforeCapabilityRequest());
        await GrainFactory
            .GetGrain<IFailingProbe>(NeuronId.For<FailingProbe>(Id.Owner, "probe").ToGrainId())
            .FailAsync();
    }
}

internal sealed class CapabilityBoundaryRecorder : Neuron, IHandle<BeforeCapabilityRequest>
{
    public Task HandleAsync(
        BeforeCapabilityRequest synapse,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class AuthorizationShapedFailureCaller
    : Neuron,
      IHandle<AuthorizationShapedFailure>
{
    public async Task HandleAsync(
        AuthorizationShapedFailure synapse,
        CancellationToken cancellationToken)
    {
        try
        {
            await GrainFactory
                .GetGrain<IFailingProbe>(NeuronId.For<FailingProbe>(Id.Owner, "probe").ToGrainId())
                .FailAuthorizationAsync();
        }
        catch (NeuronAuthorizationException)
        {
        }
    }
}
