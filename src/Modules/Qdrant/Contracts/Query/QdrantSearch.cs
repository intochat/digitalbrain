namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.search")]
public sealed record QdrantSearch(
    [property: Id(0)] float[] Vector,
    [property: Id(1)] int Limit = QdrantSearch.DefaultLimit,
    [property: Id(2)] IReadOnlyList<QdrantField>? Filter = null)
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 100;
}
