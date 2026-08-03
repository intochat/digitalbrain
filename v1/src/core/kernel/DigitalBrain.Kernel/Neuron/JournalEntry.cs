using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
[Alias("db.journal-entry")]
internal sealed record JournalEntry(
    [property: Id(0)] long Sequence,
    [property: Id(1)] SynapseDelivery Delivery);
