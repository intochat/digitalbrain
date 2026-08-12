namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.list-kinds")]
public sealed record ListKinds(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<KindsListed>;

