namespace DigitalBrain;

internal sealed record DeliveryEnvelope(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause,
    SynapseRef? Answers)
{
    internal SynapseMetadata Identity => new(Source, Sequence, Timestamp);

    internal static DeliveryEnvelope From(SynapseMetadata identity, SynapseRef? cause, SynapseRef? answers)
        => new(identity.Source, identity.Sequence, identity.Timestamp, cause, answers);
}
