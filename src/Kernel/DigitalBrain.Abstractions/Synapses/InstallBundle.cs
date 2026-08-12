namespace DigitalBrain.Abstractions;

// One request: copy structure of a named bundle as disabled instances (+ optional wires).
[GenerateSerializer]
[Alias("db.install-bundle")]
public sealed record InstallBundle(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] BundleMember[] Members,
    [property: Id(3)] BundleWire[] Wires,
    [property: Id(4)] string? Intent = null) : RequestSynapse<BundleInstalled>;

