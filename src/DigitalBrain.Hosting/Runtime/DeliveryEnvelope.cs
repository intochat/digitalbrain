namespace DigitalBrain;

internal sealed record DeliveryEnvelope(
    NeuronId Source,
    long Sequence,
    DateTimeOffset OccurredAt,
    SynapseOriginAuthority Authority,
    SynapseReference? CausedBy);
