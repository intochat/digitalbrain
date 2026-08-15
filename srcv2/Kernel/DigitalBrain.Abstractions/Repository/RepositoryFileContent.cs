namespace DigitalBrain.Abstractions.Repository;

[GenerateSerializer]
[Alias("db.repository-file-content")]
public sealed record RepositoryFileContent(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RelativePath,
    [property: Id(2)] string Content,
    [property: Id(3)] bool Truncated) : Synapse;

