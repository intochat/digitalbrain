namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.kind-record")]
public sealed record KindRecord(
    [property: Id(0)] string Kind,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string? Description,
    [property: Id(3)] string[] AcceptedKeys,
    [property: Id(4)] DateTimeOffset InstalledAt,
    [property: Id(5)] bool Builtin);

