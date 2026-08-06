using System.Text.Json;

namespace DigitalBrain;

internal sealed record StoredJournalRecord(
    long Position,
    JournalRecordDirection Direction,
    string SynapseKind,
    SynapseOrigin Origin,
    SynapseReference? CausedBy,
    DeliveryTarget[] DeliveryTargets,
    JsonElement Serialization)
{
    internal DeliveryEnvelope ToEnvelope()
        => new(Origin.Source, Origin.Sequence, Origin.OccurredAt, CausedBy);

    internal JournalRecord ToJournalRecord()
        => new(
            Position,
            Direction,
            SynapseKind,
            Origin,
            CausedBy,
            [.. DeliveryTargets.Select(static target => target.ToNeuronId())],
            Serialization.Clone());
}
