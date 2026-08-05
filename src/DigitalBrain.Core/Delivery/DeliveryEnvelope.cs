namespace DigitalBrain;

internal sealed record DeliveryEnvelope(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause,
    SynapseRef? Answers)
{
    internal SynapseMetadata Identity => new(Source, Sequence, Timestamp);
}
