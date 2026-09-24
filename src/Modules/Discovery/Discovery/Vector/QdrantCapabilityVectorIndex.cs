using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Discovery.Vector;

// One durable Qdrant collection per capability kind, named for the embedding model.
internal sealed class QdrantCapabilityVectorIndex : ICapabilityVectorIndex
{
    private const string IdField = "capability_id";
    private const string WorkspaceField = "workspace";
    private const string ModelField = "embedding_model";

    private readonly QdrantClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<CapabilityCollection, string> _ensured = [];
    private readonly Dictionary<CapabilityCollection, int> _sizes = [];

    public QdrantCapabilityVectorIndex(QdrantClient client, string embeddingModel)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        EmbeddingModel = embeddingModel;
    }

    public string EmbeddingModel { get; }

    public async Task UpsertAsync(CapabilityCollection collection, IReadOnlyList<CapabilityVectorRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(collection, records[0].Embedding.Length, cancellationToken).ConfigureAwait(false);
        var points = records.Select(record => new PointStruct
        {
            Id = ToPointId(record.Id, record.WorkspaceId),
            Vectors = record.Embedding,
            Payload =
            {
                [IdField] = record.Id,
                [WorkspaceField] = record.WorkspaceId,
                [ModelField] = EmbeddingModel,
            },
        }).ToArray();

        await _client.UpsertAsync(CollectionName(collection), points, wait: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CapabilityVectorMatch>> SearchAsync(
        CapabilityCollection collection,
        string workspaceId,
        float[] queryEmbedding,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        var name = CollectionName(collection);
        if (!await _client.CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        await EnsureCollectionAsync(collection, queryEmbedding.Length, cancellationToken).ConfigureAwait(false);
        var must = new List<Condition>();
        if (collection != CapabilityCollection.SystemCapabilities)
        {
            must.Add(MatchKeyword(WorkspaceField, workspaceId));
        }

        var results = await _client.QueryAsync(
                name,
                query: queryEmbedding,
                filter: new Filter { Must = { must } },
                limit: (ulong)take,
                payloadSelector: true,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return results
            .Select(point => new CapabilityVectorMatch(ReadString(point.Payload, IdField), point.Score))
            .ToArray();
    }

    private string CollectionName(CapabilityCollection collection) => DiscoveryCollections.NameFor(collection, EmbeddingModel);

    private async Task EnsureCollectionAsync(CapabilityCollection collection, int vectorSize, CancellationToken cancellationToken)
    {
        if (_ensured.TryGetValue(collection, out var ensured) && _sizes[collection] == vectorSize)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ensured.TryGetValue(collection, out ensured))
            {
                if (_sizes[collection] != vectorSize)
                {
                    throw new InvalidOperationException($"Collection '{ensured}' has {_sizes[collection]} dimensions, received {vectorSize}.");
                }

                return;
            }

            var name = CollectionName(collection);
            if (!await _client.CollectionExistsAsync(name, cancellationToken).ConfigureAwait(false))
            {
                await _client.CreateCollectionAsync(
                        name,
                        new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _ensured[collection] = name;
            _sizes[collection] = vectorSize;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Condition MatchKeyword(string field, string value)
        => new()
        {
            Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } },
        };

    private static PointId ToPointId(string id, string workspaceId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(id + "\0" + workspaceId));
        return new PointId { Uuid = new Guid(bytes.AsSpan(0, 16)).ToString("D") };
    }

    private static string ReadString(Google.Protobuf.Collections.MapField<string, Value> payload, string field)
        => payload.TryGetValue(field, out var value) ? value.StringValue : string.Empty;
}
