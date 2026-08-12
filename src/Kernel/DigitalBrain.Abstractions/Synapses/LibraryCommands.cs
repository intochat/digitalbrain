namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.publish-library-artifact")]
public sealed record PublishLibraryArtifact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Version,
    [property: Id(3)] string Description,
    // JSON structure: members [{grainType,localName,role,note?}], optional numbers map for demos.
    [property: Id(4)] string StructureJson,
    [property: Id(5)] ActorContext? Publisher = null) : RequestSynapse<LibraryArtifactPublished>;

[GenerateSerializer]
[Alias("db.library-artifact-published")]
public sealed record LibraryArtifactPublished(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryArtifact Artifact) : Synapse;

[GenerateSerializer]
[Alias("db.discover-library")]
public sealed record DiscoverLibrary(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Intent,
    [property: Id(2)] int Limit = 8) : RequestSynapse<LibraryDiscoveries>;

[GenerateSerializer]
[Alias("db.library-discoveries")]
public sealed record LibraryDiscoveries(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryArtifact[] Artifacts) : Synapse;

[GenerateSerializer]
[Alias("db.install-library-artifact")]
public sealed record InstallLibraryArtifact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactId,
    [property: Id(2)] ActorContext? Installer = null) : RequestSynapse<LibraryInstallRecorded>;

[GenerateSerializer]
[Alias("db.library-install-recorded")]
public sealed record LibraryInstallRecorded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall Install) : Synapse;

[GenerateSerializer]
[Alias("db.list-library-installs")]
public sealed record ListLibraryInstalls(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ActorContext? Actor = null) : RequestSynapse<LibraryInstallsListed>;

[GenerateSerializer]
[Alias("db.library-installs-listed")]
public sealed record LibraryInstallsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall[] Installs) : Synapse;

[GenerateSerializer]
[Alias("db.enable-library-install")]
public sealed record EnableLibraryInstall(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string InstallId,
    // Principal-local config (e.g. numbers) so two installs differ.
    [property: Id(2)] string? ConfigJson = null,
    [property: Id(3)] ActorContext? Actor = null) : RequestSynapse<LibraryInstallEnabled>;

[GenerateSerializer]
[Alias("db.library-install-enabled")]
public sealed record LibraryInstallEnabled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall Install) : Synapse;

[GenerateSerializer]
[Alias("db.library-artifact")]
public sealed record LibraryArtifact(
    [property: Id(0)] string ArtifactId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Version,
    [property: Id(3)] string Description,
    [property: Id(4)] string ContentHash,
    [property: Id(5)] string StructureJson,
    [property: Id(6)] PrincipalId Publisher,
    [property: Id(7)] DateTimeOffset PublishedAt);

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
