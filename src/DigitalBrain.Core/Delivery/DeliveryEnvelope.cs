namespace DigitalBrain;

internal sealed record DeliveryEnvelope(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause,
    SynapseRef? Answers,
    int Depth = 1)
{
    internal SynapseMetadata Identity => new(Source, Sequence, Timestamp);

    internal int EmissionDepth => Math.Max(1, Depth) + 1;
}
