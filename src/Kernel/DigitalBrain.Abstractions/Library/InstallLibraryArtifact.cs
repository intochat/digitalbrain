namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.install-library-artifact")]
public sealed record InstallLibraryArtifact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactId,
    [property: Id(2)] ActorContext? Installer = null) : RequestSynapse<LibraryInstallRecorded>;

