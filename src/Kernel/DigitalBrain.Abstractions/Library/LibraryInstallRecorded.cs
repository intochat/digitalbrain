namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.library-install-recorded")]
public sealed record LibraryInstallRecorded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall Install) : Synapse;

