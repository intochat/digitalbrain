namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.upsert-result")]
public sealed record QdrantUpsertResult(
    [property: Id(0)] int Stored);
