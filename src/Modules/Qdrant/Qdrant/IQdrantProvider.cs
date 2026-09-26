using DigitalBrain.Qdrant.Query;

namespace DigitalBrain.Qdrant;

// The interface between the neuron and a Qdrant server.
internal interface IQdrantProvider
{
    string ProviderName { get; }

    Task<int> UpsertAsync(IReadOnlyList<QdrantPoint> points, CancellationToken cancellationToken);

    Task<IReadOnlyList<QdrantMatch>> SearchAsync(float[] vector, int limit, IReadOnlyList<QdrantField> filter, CancellationToken cancellationToken);

    Task<int> DeleteAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken);

    Task<QdrantCollection> ReadCollectionAsync(CancellationToken cancellationToken);

    Task<QdrantConnection> PingAsync(CancellationToken cancellationToken);
}