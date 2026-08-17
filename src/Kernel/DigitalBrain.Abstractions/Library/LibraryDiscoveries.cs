namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.library-discoveries")]
public sealed record LibraryDiscoveries(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryArtifact[] Artifacts) : Synapse;

