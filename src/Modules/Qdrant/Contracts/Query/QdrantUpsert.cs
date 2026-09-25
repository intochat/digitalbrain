namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.upsert")]
public sealed record QdrantUpsert(
    [property: Id(0)] IReadOnlyList<QdrantPoint> Points)
{
    public const int MaxPoints = 128;
}
