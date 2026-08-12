namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.repository-opened")]
public sealed record RepositoryOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath,
    [property: Id(2)] int FileCount) : Synapse;

