using System.Collections.Concurrent;
using Google.Protobuf.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Qdrant;

internal sealed class QdrantStore(QdrantClient client) : IQdrant
{
    private readonly ConcurrentDictionary<string, ulong> _dimensions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _collectionGate = new(1, 1);

    public async Task Upsert(string collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) { return; }
        await EnsureCollection(collection, points[0].Vector.Length, cancellationToken).ConfigureAwait(false);
        var structs = points.Select(point =>
        {
            var pointStruct = new PointStruct { Id = QdrantPointIds.For(point.Id), Vectors = point.Vector };
            foreach (var (field, value) in point.Payload) { pointStruct.Payload[field] = value; }
            return pointStruct;
        }).ToArray();
        await client.UpsertAsync(collection, structs, wait: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorHit>> Search(string collection, float[] vector, int take,
        IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        if (!await CollectionReady(collection, vector.Length, cancellationToken).ConfigureAwait(false)) { return []; }
        var points = await client.QueryAsync(collection, query: vector, filter: Filter(mustMatch),
            limit: (ulong)take, payloadSelector: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        return [.. points.Select(static point => new VectorHit(point.Score, Payload(point.Payload)))];
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadPayload(string collection, string id, CancellationToken cancellationToken)
    {
        if (!await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false)) { return null; }
        var points = await client.RetrieveAsync(collection, QdrantPointIds.For(id), withPayload: true, withVectors: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return points.Count == 0 ? null : Payload(points[0].Payload);
    }

    public async Task<VectorPage> Scroll(string collection, IReadOnlyDictionary<string, string> mustMatch, string? cursor, int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (!await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false)) { return new([], null); }
        PointId? offset = null;
        if (cursor is not null)
        {
            if (!Guid.TryParse(cursor, out var cursorId)) { throw new ArgumentException("Invalid scroll cursor.", nameof(cursor)); }
            offset = cursorId;
        }
        var page = await client.ScrollAsync(collection, Filter(mustMatch), (uint)limit, offset,
            payloadSelector: true, vectorsSelector: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var points = page.Result.Select(static point => new VectorPoint(point.Id.Uuid,
            point.Vectors?.Vector?.GetDenseVector()?.Data.ToArray() ?? throw new InvalidOperationException("A scrolled point has no dense vector."),
            Payload(point.Payload))).ToArray();
        return new(points, page.NextPageOffset?.Uuid);
    }

    public async Task Delete(string collection, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0 || !await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false)) { return; }
        await client.DeleteAsync(collection, [.. ids.Select(QdrantPointIds.For)], wait: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> DeleteWhere(string collection, IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken)
    {
        if (mustMatch.Count == 0) { throw new ArgumentException("Deleting by filter requires at least one condition.", nameof(mustMatch)); }
        if (!await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false)) { return 0; }
        var filter = Filter(mustMatch);
        var count = await client.CountAsync(collection, filter, exact: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (count == 0) { return 0; }
        await client.DeleteAsync(collection, filter, wait: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (long)count;
    }

    private async Task<bool> CollectionReady(string collection, int dimensions, CancellationToken cancellationToken)
    {
        if (!_dimensions.ContainsKey(collection) && !await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
        { return false; }
        await EnsureCollection(collection, dimensions, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task EnsureCollection(string collection, int dimensions, CancellationToken cancellationToken)
    {
        if (dimensions == 0) { throw new ArgumentException("A vector needs at least one dimension."); }
        if (!_dimensions.TryGetValue(collection, out var known))
        {
            await _collectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_dimensions.TryGetValue(collection, out known))
                {
                    if (await client.CollectionExistsAsync(collection, cancellationToken).ConfigureAwait(false))
                    {
                        var info = await client.GetCollectionInfoAsync(collection, cancellationToken).ConfigureAwait(false);
                        known = info.Config?.Params?.VectorsConfig?.Params?.Size
                            ?? throw new InvalidOperationException($"Collection '{collection}' has no fixed vector size.");
                    }
                    else
                    {
                        await client.CreateCollectionAsync(collection,
                            new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine },
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        known = (ulong)dimensions;
                    }
                    _dimensions[collection] = known;
                }
            }
            finally { _collectionGate.Release(); }
        }
        if (known != (ulong)dimensions)
        { throw new InvalidOperationException($"Collection '{collection}' holds {known}-dimension vectors, received {dimensions}."); }
    }

    private static Filter Filter(IReadOnlyDictionary<string, string> mustMatch)
    {
        var filter = new Filter();
        foreach (var (field, value) in mustMatch) { filter.Must.Add(Conditions.MatchKeyword(field, value)); }
        return filter;
    }

    private static Dictionary<string, string> Payload(MapField<string, Value> payload)
        => payload.ToDictionary(static field => field.Key, static field => field.Value.StringValue, StringComparer.Ordinal);
}
