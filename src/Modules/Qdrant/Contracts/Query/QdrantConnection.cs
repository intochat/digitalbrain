namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer]
[Alias("db.qdrant.connection")]
public sealed record QdrantConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string Collection,
    [property: Id(2)] string? Detail,
    [property: Id(3)] string Provider);
