namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.library-artifact-published")]
public sealed record LibraryArtifactPublished(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] LibraryArtifact Artifact) : Synapse;

