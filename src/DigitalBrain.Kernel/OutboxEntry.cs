using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.outbox-entry")]
internal sealed record OutboxEntry(
    [property: Id(0)] Synapse Synapse,
    [property: Id(1)] NeuronId[] Pending);
