using DigitalBrain.Qdrant.Query;

namespace DigitalBrain.Qdrant;

// In-memory IQdrantProvider so the neuron tests never touch a real server.
internal sealed class FakeQdrantProvider : IQdrantProvider
{
    private readonly Dictionary<string, QdrantPoint> _points = new(StringComparer.Ordinal);
    private bool _collectionExists;

    public string ProviderName => "Fake";
    public int VectorSize { get; init; } = 3;
    public bool Connected { get; set; } = true;

    public Task<int> UpsertAsync(IReadOnlyList<QdrantPoint> points, CancellationToken cancellationToken)
    {
        if (!Connected)
        {
            throw new QdrantQueryException("Qdrant is unreachable.");
        }

        foreach (var point in points)
        {
            QdrantPointRules.RequireVector(point.Vector, VectorSize);
            var id = QdrantPointRules.RequirePointId(point.Id).ToString("D");
            _points[id] = point with { Id = id };
        }

        _collectionExists = true;
        return Task.FromResult(points.Count);
    }

    public Task<IReadOnlyList<QdrantMatch>> SearchAsync(
        float[] vector, int limit, IReadOnlyList<QdrantField> filter, CancellationToken cancellationToken)
    {
        QdrantPointRules.RequireVector(vector, VectorSize);
        if (!_collectionExists)
        {
            return Task.FromResult<IReadOnlyList<QdrantMatch>>([]);
        }

        IReadOnlyList<QdrantMatch> matches = _points.Values
            .Where(point => filter.All(field => point.Payload.Any(stored => stored.Name == field.Name && stored.Value == field.Value)))
            .Take(limit)
            .Select(point => new QdrantMatch(point.Id, 1f, point.Payload))
            .ToArray();
        return Task.FromResult(matches);
    }

    public Task<int> DeleteAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (!_collectionExists)
        {
            return Task.FromResult(0);
        }

        foreach (var id in ids)
        {
            _points.Remove(QdrantPointRules.RequirePointId(id).ToString("D"));
        }

        return Task.FromResult(ids.Count);
    }

    public Task<QdrantCollection> ReadCollectionAsync(CancellationToken cancellationToken)
        => Task.FromResult(new QdrantCollection(QdrantNames.CollectionName, VectorSize, _points.Count, _collectionExists));

    public Task<QdrantConnection> PingAsync(CancellationToken cancellationToken)
        => Task.FromResult(new QdrantConnection(Connected, QdrantNames.CollectionName, Connected ? "fake" : null, ProviderName));
}