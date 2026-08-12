namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.read-repository-file")]
public sealed record ReadRepositoryFile(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RelativePath,
    [property: Id(2)] int MaxChars = 4000) : RequestSynapse<RepositoryFileContent>;

