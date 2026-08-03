namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.synapse-delivery")]
public sealed class SynapseDelivery
{
    internal SynapseDelivery(
        Synapse synapse,
        SynapseId synapseId,
        CorrelationId correlationId,
        SynapseId? causationId,
        NeuronId caller,
        long sequence,
        DateTimeOffset timestamp)
    {
        Synapse = synapse;
        SynapseId = synapseId;
        CorrelationId = correlationId;
        CausationId = causationId;
        Caller = caller;
        Sequence = sequence;
        Timestamp = timestamp;
    }

    [Id(0)]
    public Synapse Synapse { get; }

    [Id(1)]
    public SynapseId SynapseId { get; }

    [Id(2)]
    public CorrelationId CorrelationId { get; }

    [Id(3)]
    public SynapseId? CausationId { get; }

    [Id(4)]
    public NeuronId Caller { get; }

    [Id(5)]
    public long Sequence { get; }

    [Id(6)]
    public DateTimeOffset Timestamp { get; }

    internal static SynapseDelivery Create(
        Synapse synapse,
        NeuronId caller,
        long sequence,
        SynapseDelivery? cause = null,
        TimeProvider? timeProvider = null,
        CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);

        return new(
            synapse,
            SynapseId.New(),
            correlation ?? cause?.CorrelationId ?? CorrelationId.New(),
            cause?.SynapseId,
            caller,
            sequence,
            (timeProvider ?? TimeProvider.System).GetUtcNow());
    }
}
