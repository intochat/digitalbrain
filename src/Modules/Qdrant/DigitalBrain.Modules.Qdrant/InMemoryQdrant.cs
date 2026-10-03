using System.Collections.Concurrent;
using DigitalBrain.Sdk.Vectors;

namespace DigitalBrain.Qdrant;

// The same contract without a server: hosts that compose no Qdrant connection, and tests.
public sealed class InMemoryQdrant : IVectorStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, VectorPoint>> _collections = new(StringComparer.Ordinal);

    public Task Upsert(string collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        var stored = _collections.GetOrAdd(collection, static _ => new());
        foreach (var point in points)
        {
            if (!stored.IsEmpty && stored.Values.First().Vector.Length != point.Vector.Length)
            { throw new InvalidOperationException($"Collection '{collection}' holds vectors of another dimension."); }
            var pointId = QdrantPointIds.For(point.Id);
            stored[pointId] = point with { Id = pointId.ToString("D") };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorHit>> Search(string collection, float[] vector, int take,
        IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken)
    {
        IReadOnlyList<VectorHit> hits = [.. Matching(collection, mustMatch)
            .Select(point => new VectorHit(Cosine(point.Vector, vector), point.Payload))
            .OrderByDescending(static hit => hit.Score)
            .Take(take)];
        return Task.FromResult(hits);
    }

    public Task<IReadOnlyDictionary<string, string>?> ReadPayload(string collection, string id, CancellationToken cancellationToken)
        => Task.FromResult(_collections.TryGetValue(collection, out var stored) && stored.TryGetValue(QdrantPointIds.For(id), out var point)
            ? point.Payload
            : null);

    public Task<VectorPage> Scroll(string collection, IReadOnlyDictionary<string, string> mustMatch, string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        var page = Matching(collection, mustMatch)
            .OrderBy(static point => point.Id, StringComparer.Ordinal)
            .SkipWhile(point => cursor is not null && string.CompareOrdinal(point.Id, cursor) < 0)
            .Take(limit + 1)
            .ToArray();
        return Task.FromResult(new VectorPage([.. page.Take(limit)], page.Length > limit ? page[^1].Id : null));
    }

    public Task Delete(string collection, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (_collections.TryGetValue(collection, out var stored))
        {
            foreach (var id in ids) { stored.TryRemove(QdrantPointIds.For(id), out _); }
        }
        return Task.CompletedTask;
    }

    public Task<long> DeleteWhere(string collection, IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken)
    {
        if (!_collections.TryGetValue(collection, out var stored)) { return Task.FromResult(0L); }
        var removed = Matching(collection, mustMatch).Count(point => stored.TryRemove(Guid.Parse(point.Id), out _));
        return Task.FromResult((long)removed);
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length) { throw new InvalidOperationException("Vectors differ in dimension."); }
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }
        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / Math.Sqrt(leftNorm * rightNorm);
    }

    private VectorPoint[] Matching(string collection, IReadOnlyDictionary<string, string> mustMatch)
        => _collections.TryGetValue(collection, out var stored)
            ? [.. stored.Values.Where(point => mustMatch.All(condition =>
                point.Payload.TryGetValue(condition.Key, out var value) && value == condition.Value))]
            : [];
}
