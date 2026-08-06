using System.Text.Json;

namespace DigitalBrain;

public sealed record JournalRecord(
    long Position,
    JournalRecordDirection Direction,
    string SynapseKind,
    SynapseOrigin Origin,
    SynapseReference? CausedBy,
    IReadOnlyList<NeuronId> DeliveryTargets,
    JsonElement Serialization);
