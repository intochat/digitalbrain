using Qdrant.Client;

namespace DigitalBrain.Product.Memory.Qdrant;

/// <summary>
/// Creates a provider-neutral <see cref="IMemoryStore"/> bound to a physical
/// Hosting workspace. Register its <see cref="CreateForWorkspace"/> result as
/// the workspace service; callers never supply workspace identity in memory
/// entries or queries.
/// </summary>
public sealed class QdrantMemoryStoreFactory : IDisposable
{
    private readonly QdrantMemoryBackend backend;

    public QdrantMemoryStoreFactory(
        QdrantClient client,
        ITextEmbeddingGenerator embeddings,
        QdrantMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(options);

        backend = new QdrantMemoryBackend(client, embeddings, options);
    }

    public IMemoryStore CreateForWorkspace(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        return backend.CreateWorkspaceStore(workspaceId);
    }

    public void Dispose() => backend.Dispose();
}
