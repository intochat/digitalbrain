using DigitalBrain.Qdrant;

namespace DigitalBrain.Qdrant.Aspire.Hosting;

public sealed class QdrantHostingOptions
{
    public bool PersistentStorage { get; set; } = true;
    public string ConnectionName { get; set; } = QdrantRegistration.DefaultConnectionName;
    public string CollectionName { get; set; } = QdrantNames.CollectionName;
    public int VectorSize { get; set; } = QdrantNames.DefaultVectorSize;
}