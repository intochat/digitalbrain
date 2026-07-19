using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.synapse-metadata")]
public sealed record SynapseMetadata(
    [property: Id(0)] SynapseId SynapseId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] SynapseId? CausationId,
    [property: Id(3)] NeuronId Caller,
    [property: Id(4)] NeuronId? Receiver,
    [property: Id(5)] RoutingMode RoutingMode,
    [property: Id(6)] DateTimeOffset Timestamp)
{
    public static SynapseMetadata ForSend(
        NeuronId caller,
        NeuronId receiver,
        SynapseMetadata? cause = null,
        TimeProvider? timeProvider = null)
        => Stamp(caller, receiver, RoutingMode.PointToPoint, cause, timeProvider);

    public static SynapseMetadata ForBroadcast(
        NeuronId caller,
        SynapseMetadata? cause = null,
        TimeProvider? timeProvider = null)
        => Stamp(caller, null, RoutingMode.Broadcast, cause, timeProvider);

    public static SynapseMetadata ForReply(
        NeuronId caller,
        SynapseMetadata cause,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(cause);

        return Stamp(caller, cause.Caller, RoutingMode.PointToPoint, cause, timeProvider);
    }

    private static SynapseMetadata Stamp(
        NeuronId caller,
        NeuronId? receiver,
        RoutingMode routingMode,
        SynapseMetadata? cause,
        TimeProvider? timeProvider)
        => new(
            SynapseId.New(),
            cause?.CorrelationId ?? CorrelationId.New(),
            cause?.SynapseId,
            caller,
            receiver,
            routingMode,
            (timeProvider ?? TimeProvider.System).GetUtcNow());
}
