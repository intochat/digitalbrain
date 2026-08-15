namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.bundle-member")]
public sealed record BundleMember(
    [property: Id(0)] string GrainType,
    [property: Id(1)] string Name,
    [property: Id(2)] string Role,
    [property: Id(3)] string? Note = null);

