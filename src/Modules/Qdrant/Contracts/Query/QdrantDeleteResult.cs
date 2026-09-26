namespace DigitalBrain.Qdrant.Query;

// Accepted is how many ids Qdrant took. A missing id still counts: delete is idempotent.
[GenerateSerializer]
[Alias("db.qdrant.delete-result")]
public sealed record QdrantDeleteResult(
    [property: Id(0)] int Accepted);