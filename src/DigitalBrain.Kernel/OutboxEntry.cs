using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

[GenerateSerializer]
[Alias("db.outbox-entry")]
internal sealed record OutboxEntry(
    [property: Id(0)] Synapse Synapse,
    [property: Id(1)] NeuronId[] Pending,
    [property: Id(2)] int Depth,
    [property: Id(3)] int Attempts,
    [property: Id(4)] DateTimeOffset FirstAttempted);
