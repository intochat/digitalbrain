namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.delete")]
public sealed record QdrantDelete(
    [property: Id(0)] IReadOnlyList<string> Ids)
{
    public const int MaxIds = 128;
}