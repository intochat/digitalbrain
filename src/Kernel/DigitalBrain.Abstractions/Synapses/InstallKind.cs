namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.install-kind")]
public sealed record InstallKind(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string? Description = null,
    [property: Id(4)] string[]? AcceptedKeys = null) : RequestSynapse<KindInstalled>;

