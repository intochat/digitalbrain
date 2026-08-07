using System.Text;

namespace DigitalBrain.Product.Memory.Qdrant;

/// <summary>
/// Physical configuration for one Qdrant-backed memory store. The workspace
/// isolation secret turns a Hosting-issued workspace key into an opaque storage
/// partition; it is never placed in a product fact or output.
/// </summary>
public sealed class QdrantMemoryOptions
{
    public QdrantMemoryOptions(string collectionName, string workspaceIsolationSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceIsolationSecret);

        CollectionName = collectionName.Trim();
        WorkspaceIsolationSecret = Encoding.UTF8.GetBytes(workspaceIsolationSecret);
    }

    public string CollectionName { get; }

    internal byte[] WorkspaceIsolationSecret { get; }
}
