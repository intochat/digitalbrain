using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.accepted`1")]
public sealed record Accepted<TReceipt>(
    [property: Id(0)] TReceipt Receipt,
    [property: Id(1)] SignalId Work);
