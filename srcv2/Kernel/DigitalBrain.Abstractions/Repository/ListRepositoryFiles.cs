namespace DigitalBrain.Abstractions.Repository;

[GenerateSerializer]
[Alias("db.list-repository-files")]
public sealed record ListRepositoryFiles(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string? Extension = ".cs",
    [property: Id(2)] int Limit = 30) : RequestSynapse<RepositoryFilesListed>;

