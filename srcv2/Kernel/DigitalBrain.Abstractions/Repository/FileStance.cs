namespace DigitalBrain.Abstractions.Repository;

[GenerateSerializer]
[Alias("db.file-stance")]
public sealed record FileStance(
    [property: Id(0)] string RelativePath,
    [property: Id(1)] string Stance,
    [property: Id(2)] string Rationale,
    [property: Id(3)] int Priority);

