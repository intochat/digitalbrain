namespace DigitalBrain.Qdrant.Query;

[GenerateSerializer, Alias("db.qdrant.failed")]
public sealed class QdrantQueryException(string message) : InvalidOperationException(message);