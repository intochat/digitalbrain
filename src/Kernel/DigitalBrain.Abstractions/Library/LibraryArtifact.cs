namespace DigitalBrain.Abstractions.Library;

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

