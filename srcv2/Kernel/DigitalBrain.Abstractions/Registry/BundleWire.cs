namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.bundle-wire")]
public sealed record BundleWire(
    [property: Id(0)] string SourceType,
    [property: Id(1)] string SourceName,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] string TargetType,
    [property: Id(4)] string TargetName,
    [property: Id(5)] string? Transform = null);

