using DigitalBrain.Memory.Qdrant;

namespace DigitalBrain.Memory.Aspire.Hosting;

public sealed class QdrantHostingOptions
{
    public string ResourceName { get; set; } = "qdrant";
    public string ConnectionName { get; set; } = QdrantVectorMemoryRegistration.DefaultConnectionName;
    public string? CollectionName { get; set; }
}