namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.library-install")]
public sealed record LibraryInstall(
    [property: Id(0)] string InstallId,
    [property: Id(1)] string ArtifactId,
    [property: Id(2)] string Name,
    [property: Id(3)] string Version,
    [property: Id(4)] string ContentHash,
    [property: Id(5)] PrincipalId Installer,
    [property: Id(6)] bool Enabled,
    [property: Id(7)] string? ConfigJson,
    [property: Id(8)] DateTimeOffset InstalledAt,
    [property: Id(9)] DateTimeOffset? EnabledAt);

