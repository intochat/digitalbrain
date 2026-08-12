namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.library-install-enabled")]
public sealed record LibraryInstallEnabled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryInstall Install) : Synapse;

