namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.search-result")]
public sealed record QdrantSearchResult(
    [property: Id(0)] IReadOnlyList<QdrantMatch> Matches);