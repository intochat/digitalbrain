namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.match")]
public sealed record QdrantMatch(
    [property: Id(0)] string Id,
    [property: Id(1)] float Score,
    [property: Id(2)] IReadOnlyList<QdrantField> Payload);