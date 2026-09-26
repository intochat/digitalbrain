namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.point")]
public sealed record QdrantPoint(
    [property: Id(0)] string Id,
    [property: Id(1)] float[] Vector,
    [property: Id(2)] IReadOnlyList<QdrantField> Payload);