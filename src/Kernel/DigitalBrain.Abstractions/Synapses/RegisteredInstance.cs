namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.registered-instance")]
public sealed record RegisteredInstance(
    [property: Id(0)] NeuronId Subject,
    [property: Id(1)] string Role,
    [property: Id(2)] string? Bundle,
    [property: Id(3)] bool Enabled,
    [property: Id(4)] DateTimeOffset RegisteredAt,
    [property: Id(5)] string? Note);

