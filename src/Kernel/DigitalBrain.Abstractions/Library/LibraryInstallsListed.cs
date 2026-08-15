namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.library-installs-listed")]
public sealed record LibraryInstallsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall[] Installs) : Synapse;

