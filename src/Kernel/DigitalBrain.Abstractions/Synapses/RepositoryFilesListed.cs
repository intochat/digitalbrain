namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.repository-files-listed")]
public sealed record RepositoryFilesListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string[] RelativePaths) : Synapse;

