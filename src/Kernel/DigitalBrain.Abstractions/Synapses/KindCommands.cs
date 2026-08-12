namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.install-kind")]
public sealed record InstallKind(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string? Description = null,
    [property: Id(4)] string[]? AcceptedKeys = null) : RequestSynapse<KindInstalled>;

[GenerateSerializer]
[Alias("db.kind-installed")]
public sealed record KindInstalled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] KindRecord Kind) : Synapse;

[GenerateSerializer]
[Alias("db.list-kinds")]
public sealed record ListKinds(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<KindsListed>;

[GenerateSerializer]
[Alias("db.kinds-listed")]
public sealed record KindsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] KindRecord[] Kinds) : Synapse;

[GenerateSerializer]
[Alias("db.kind-record")]
public sealed record KindRecord(
    [property: Id(0)] string Kind,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string? Description,
    [property: Id(3)] string[] AcceptedKeys,
    [property: Id(4)] DateTimeOffset InstalledAt,
    [property: Id(5)] bool Builtin);
