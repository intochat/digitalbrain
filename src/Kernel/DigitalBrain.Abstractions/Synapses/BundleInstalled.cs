namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.bundle-installed")]
public sealed record BundleInstalled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] int MemberCount,
    [property: Id(3)] int WireCount,
    [property: Id(4)] bool Enabled) : Synapse;

