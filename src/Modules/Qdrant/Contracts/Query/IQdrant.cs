using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Qdrant.Query;

[Alias("qdrant")]
public interface IQdrant : INeuron
{
    [Alias("upsert")]
    Task<QdrantUpsertResult> Upsert(QdrantUpsert request, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("search")]
    Task<QdrantSearchResult> Search(QdrantSearch request, CancellationToken cancellationToken = default);

    [Alias("delete")]
    Task<QdrantDeleteResult> Delete(QdrantDelete request, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("collection")]
    Task<QdrantCollection> ReadCollection(CancellationToken cancellationToken = default);

    [ReadOnly, Alias("connection")]
    Task<QdrantConnection> ReadConnection(CancellationToken cancellationToken = default);
}
