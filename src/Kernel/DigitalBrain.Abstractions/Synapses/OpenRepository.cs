namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.open-repository")]
public sealed record OpenRepository(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string RootPath) : RequestSynapse<RepositoryOpened>;

