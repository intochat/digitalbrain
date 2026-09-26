namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.collection")]
public sealed record QdrantCollection(
    [property: Id(0)] string Name,
    [property: Id(1)] int VectorSize,
    [property: Id(2)] long PointsCount,
    [property: Id(3)] bool Exists);