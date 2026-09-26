namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.field")]
public sealed record QdrantField(
    [property: Id(0)] string Name,
    [property: Id(1)] string Value);