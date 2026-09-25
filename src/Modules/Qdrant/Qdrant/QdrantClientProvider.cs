using DigitalBrain.Qdrant.Query;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Qdrant;

// One collection, created on the first upsert at the configured width. Distance is cosine.
internal sealed class QdrantClientProvider(QdrantClient client, string collectionName, int vectorSize) : IQdrantProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    public string ProviderName => QdrantModule.ProviderName;

    public async Task<int> UpsertAsync(IReadOnlyList<QdrantPoint> points, CancellationToken cancellationToken)
    {
        var stored = points.Select(ToPoint).ToArray();
        await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await client.UpsertAsync(collectionName, stored, wait: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return stored.Length;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QdrantQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant refused the upsert.");
        }
    }

    public async Task<IReadOnlyList<QdrantMatch>> SearchAsync(
        float[] vector, int limit, IReadOnlyList<QdrantField> filter, CancellationToken cancellationToken)
    {
        QdrantPointRules.RequireVector(vector, vectorSize);
        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        await EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        var conditions = filter.Select(static field => MatchKeyword(field.Name, field.Value)).ToArray();
        Filter? queryFilter = conditions.Length == 0 ? null : new Filter { Must = { conditions } };
        try
        {
            var results = await client.QueryAsync(
                    collectionName,
                    query: vector,
                    filter: queryFilter,
                    limit: (ulong)limit,
                    payloadSelector: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return results.Select(ToMatch).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QdrantQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant refused the search.");
        }
    }

    public async Task<int> DeleteAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var pointIds = ids.Select(static id => new PointId { Uuid = QdrantPointRules.RequirePointId(id).ToString("D") }).ToArray();
        try
        {
            await client.DeleteAsync(collectionName, ids: pointIds, wait: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            return pointIds.Length;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QdrantQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant refused the delete.");
        }
    }

    public async Task<QdrantCollection> ReadCollectionAsync(CancellationToken cancellationToken)
    {
        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return new QdrantCollection(collectionName, vectorSize, 0, false);
        }

        try
        {
            var info = await client.GetCollectionInfoAsync(collectionName, cancellationToken).ConfigureAwait(false);
            return new QdrantCollection(collectionName, (int)ReadVectorSize(info), (long)info.PointsCount, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QdrantQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant refused the collection read.");
        }
    }

    public async Task<QdrantConnection> PingAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var health = await client.HealthAsync(deadline.Token).ConfigureAwait(false);
            return new QdrantConnection(true, collectionName, health.Version, ProviderName);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new QdrantConnection(false, collectionName, null, ProviderName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new QdrantConnection(false, collectionName, null, ProviderName);
        }
    }

    private PointStruct ToPoint(QdrantPoint point)
    {
        QdrantPointRules.RequireVector(point.Vector, vectorSize);
        var stored = new PointStruct
        {
            Id = new PointId { Uuid = QdrantPointRules.RequirePointId(point.Id).ToString("D") },
            Vectors = point.Vector,
        };
        foreach (var field in point.Payload)
        {
            stored.Payload[field.Name] = field.Value;
        }

        return stored;
    }

    private static QdrantMatch ToMatch(ScoredPoint point)
    {
        var payload = new List<QdrantField>(point.Payload.Count);
        foreach (var (name, value) in point.Payload)
        {
            if (value.KindCase == Value.KindOneofCase.StringValue)
            {
                payload.Add(new QdrantField(name, value.StringValue));
            }
        }

        return new QdrantMatch(point.Id.Uuid, point.Score, payload);
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ready)
            {
                return;
            }

            if (await client.CollectionExistsAsync(collectionName, cancellationToken).ConfigureAwait(false))
            {
                var info = await client.GetCollectionInfoAsync(collectionName, cancellationToken).ConfigureAwait(false);
                var existing = ReadVectorSize(info);
                if (existing != (ulong)vectorSize)
                {
                    throw new QdrantQueryException($"collection '{collectionName}' has {existing} dimensions, configured width is {vectorSize}.");
                }

                _ready = true;
                return;
            }

            await client.CreateCollectionAsync(
                    collectionName,
                    new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _ready = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QdrantQueryException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant refused to open the collection.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await client.CollectionExistsAsync(collectionName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QdrantQueryException("Qdrant is unreachable.");
        }
    }

    private static ulong ReadVectorSize(CollectionInfo info)
    {
        if (info.Config?.Params?.VectorsConfig?.Params is { } vectors)
        {
            return vectors.Size;
        }

        throw new QdrantQueryException("collection does not use one dense vector.");
    }

    private static Condition MatchKeyword(string field, string value)
        => new()
        {
            Field = new FieldCondition
            {
                Key = field,
                Match = new Match { Keyword = value },
            },
        };
}
