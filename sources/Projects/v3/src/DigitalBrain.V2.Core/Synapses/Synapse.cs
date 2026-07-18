namespace DigitalBrain.V2.Core.Synapses;

// The only message type. Immutable record; an open hierarchy (capsules add their own
// `: Synapse` records), which is exactly why synapses are NOT a closed C# union.
[GenerateSerializer]
public abstract record Synapse
{
    [Id(0)] public Guid SynapseId { get; init; } = Guid.NewGuid();
    [Id(1)] public Guid CorrelationId { get; init; }
    [Id(2)] public Guid? CausationId { get; init; }
    [Id(3)] public NeuronId Caller { get; init; } = NeuronId.None;
    [Id(4)] public NeuronId Receiver { get; init; } = NeuronId.None;
    [Id(5)] public RoutingMode Routing { get; init; } = RoutingMode.PointToPoint;
    [Id(6)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Fills only the header fields the synapse did not set, inheriting correlation and
    // causation from the synapse currently being handled so the timeline stays connected.
    public Synapse Stamp(NeuronId firing, RoutingMode routing, Synapse? incoming) => this with
    {
        Caller = Caller.IsNone ? firing : Caller,
        Routing = routing,
        CorrelationId = CorrelationId != default ? CorrelationId : incoming?.CorrelationId ?? Guid.NewGuid(),
        CausationId = CausationId ?? incoming?.SynapseId,
        Timestamp = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp
    };
}
