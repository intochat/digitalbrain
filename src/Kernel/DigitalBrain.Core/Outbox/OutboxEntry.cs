using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.outbox-entry")]
internal sealed record OutboxEntry(
    [property: Id(0)] SynapseDelivery Delivery,
    [property: Id(1)] NeuronId[] Pending,
    [property: Id(2)] int Depth,
    [property: Id(3)] int Attempts);
