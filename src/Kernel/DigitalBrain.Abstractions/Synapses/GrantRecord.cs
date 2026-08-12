namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.grant-record")]
public sealed record GrantRecord(
    [property: Id(0)] PrincipalId Grantee,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] GrantKind Kind,
    [property: Id(3)] PrincipalId Grantor,
    [property: Id(4)] DateTimeOffset At,
    [property: Id(5)] string? Intent);

