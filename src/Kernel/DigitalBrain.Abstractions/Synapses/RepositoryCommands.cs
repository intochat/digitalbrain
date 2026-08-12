namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.open-repository")]
public sealed record OpenRepository(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath) : RequestSynapse<RepositoryOpened>;

[GenerateSerializer]
[Alias("db.repository-opened")]
public sealed record RepositoryOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath,
    [property: Id(2)] int FileCount) : Synapse;

[GenerateSerializer]
[Alias("db.list-repository-files")]
public sealed record ListRepositoryFiles(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string? Extension = ".cs",
    [property: Id(2)] int Limit = 30) : RequestSynapse<RepositoryFilesListed>;

[GenerateSerializer]
[Alias("db.repository-files-listed")]
public sealed record RepositoryFilesListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string[] RelativePaths) : Synapse;

[GenerateSerializer]
[Alias("db.read-repository-file")]
public sealed record ReadRepositoryFile(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RelativePath,
    [property: Id(2)] int MaxChars = 4000) : RequestSynapse<RepositoryFileContent>;

[GenerateSerializer]
[Alias("db.repository-file-content")]
public sealed record RepositoryFileContent(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RelativePath,
    [property: Id(2)] string Content,
    [property: Id(3)] bool Truncated) : Synapse;
