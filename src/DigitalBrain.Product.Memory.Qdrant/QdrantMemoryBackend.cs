using System.Security.Cryptography;
using System.Text;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DigitalBrain.Product.Memory.Qdrant;

internal sealed class QdrantMemoryBackend(
    QdrantClient client,
    ITextEmbeddingGenerator embeddings,
    QdrantMemoryOptions options) : IDisposable
{
    private const string WorkspaceField = "db_workspace";
    private const string EntryIdField = "db_entry_id";
    private const string ContentField = "db_content";
    private const string MetadataPrefix = "db_meta_";

    private readonly SemaphoreSlim collectionGate = new(1, 1);
    private int? vectorDimension;
    private bool collectionReady;

    internal QdrantMemoryStore CreateWorkspaceStore(string workspaceId)
        => new(this, WorkspaceTokenOf(workspaceId));

    internal async Task StoreAsync(string workspaceToken, MemoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceToken);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var embedding = await EmbedAsync(entry.Content, cancellationToken).ConfigureAwait(false);
        await EnsureCollectionAsync(embedding.Values.Count, cancellationToken).ConfigureAwait(false);

        var point = new PointStruct
        {
            Id = PointIdOf(workspaceToken, entry.Id),
            Vectors = embedding.Values.ToArray(),
            Payload =
            {
                [WorkspaceField] = workspaceToken,
                [EntryIdField] = entry.Id,
                [ContentField] = entry.Content,
            },
        };
        foreach (var (key, value) in entry.Metadata)
        {
            point.Payload[MetadataFieldOf(key)] = value;
        }

        try
        {
            await client.UpsertAsync(options.CollectionName, [point], wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }
    }

    internal async Task<IReadOnlyList<MemoryHit>> SearchAsync(
        string workspaceToken,
        MemoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceToken);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var embedding = await EmbedAsync(query.Text, cancellationToken).ConfigureAwait(false);
        await EnsureCollectionAsync(embedding.Values.Count, cancellationToken).ConfigureAwait(false);
        var conditions = new List<Condition> { MatchKeyword(WorkspaceField, workspaceToken) };
        conditions.AddRange(query.Metadata.Select(static filter => MatchKeyword(MetadataFieldOf(filter.Key), filter.Value)));

        IReadOnlyList<ScoredPoint> results;
        try
        {
            results = await client.SearchAsync(
                    options.CollectionName,
                    embedding.Values.ToArray(),
                    filter: new Filter { Must = { conditions } },
                    limit: (ulong)query.MaximumResults,
                    payloadSelector: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }

        var hits = new List<MemoryHit>(results.Count);
        foreach (var point in results)
        {
            if (TryReadHit(point, workspaceToken) is { } hit)
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    internal async Task RemoveAsync(string workspaceToken, string entryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var pointId = PointIdOf(workspaceToken, entryId);
        IReadOnlyList<RetrievedPoint> existing;
        try
        {
            existing = await client.RetrieveAsync(
                    options.CollectionName,
                    [pointId],
                    withPayload: true,
                    withVectors: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }

        if (existing.Count == 0
            || !PayloadEquals(existing[0].Payload, WorkspaceField, workspaceToken)
            || !PayloadEquals(existing[0].Payload, EntryIdField, entryId))
        {
            return;
        }

        try
        {
            await client.DeleteAsync(options.CollectionName, pointId, wait: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }
    }

    private async Task<MemoryEmbedding> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            return await embeddings.EmbedAsync(text, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Memory embeddings are unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory embeddings are unavailable.");
        }
    }

    private async Task EnsureCollectionAsync(int requestedDimension, CancellationToken cancellationToken)
    {
        if (collectionReady)
        {
            ValidateDimension(requestedDimension);
            return;
        }

        await collectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (collectionReady)
            {
                ValidateDimension(requestedDimension);
                return;
            }

            if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await client.CreateCollectionAsync(
                            options.CollectionName,
                            new VectorParams
                            {
                                Size = (ulong)requestedDimension,
                                Distance = Distance.Cosine,
                            },
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Memory storage is temporarily unavailable.");
                    }
                }
            }

            var information = await CollectionInfoAsync(cancellationToken).ConfigureAwait(false);
            var actualDimension = ReadVectorDimension(information);
            if (actualDimension != (ulong)requestedDimension)
            {
                throw new InvalidOperationException("Memory embedding dimension does not match existing storage.");
            }

            try
            {
                await client.CreatePayloadIndexAsync(
                        options.CollectionName,
                        WorkspaceField,
                        PayloadSchemaType.Keyword,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Memory storage is temporarily unavailable.");
            }

            vectorDimension = requestedDimension;
            collectionReady = true;
        }
        finally
        {
            collectionGate.Release();
        }
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await client.CollectionExistsAsync(options.CollectionName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }
    }

    private async Task<CollectionInfo> CollectionInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetCollectionInfoAsync(options.CollectionName, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Memory storage is temporarily unavailable.");
        }
    }

    private void ValidateDimension(int requestedDimension)
    {
        if (vectorDimension != requestedDimension)
        {
            throw new InvalidOperationException("Memory embedding dimension does not match existing storage.");
        }
    }

    public void Dispose() => collectionGate.Dispose();

    private static MemoryHit? TryReadHit(ScoredPoint point, string workspaceToken)
    {
        var payload = point.Payload;
        if (!PayloadEquals(payload, WorkspaceField, workspaceToken))
        {
            return null;
        }

        var entryId = ReadString(payload, EntryIdField);
        var content = ReadString(payload, ContentField);
        if (string.IsNullOrWhiteSpace(entryId) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, value) in payload)
        {
            if (field.StartsWith(MetadataPrefix, StringComparison.Ordinal)
                && TryReadMetadataKey(field, out var key)
                && !string.IsNullOrWhiteSpace(value.StringValue))
            {
                metadata[key] = value.StringValue;
            }
        }

        return new MemoryHit(
            new MemoryEntry(entryId, content, metadata),
            NormalizeScore(point.Score));
    }

    private string WorkspaceTokenOf(string workspaceId)
        => Convert.ToHexString(HMACSHA256.HashData(
            options.WorkspaceIsolationSecret,
            Encoding.UTF8.GetBytes(workspaceId)));

    private PointId PointIdOf(string workspaceToken, string entryId)
    {
        var bytes = HMACSHA256.HashData(
            options.WorkspaceIsolationSecret,
            Encoding.UTF8.GetBytes(workspaceToken + "\0" + entryId));
        return new PointId { Uuid = new Guid(bytes.AsSpan(0, 16)).ToString("D") };
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

    private static string MetadataFieldOf(string key)
        => MetadataPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryReadMetadataKey(string field, out string key)
    {
        var encoded = field[MetadataPrefix.Length..]
            .Replace('-', '+')
            .Replace('_', '/');
        var padding = (4 - (encoded.Length % 4)) % 4;
        encoded = encoded.PadRight(encoded.Length + padding, '=');
        try
        {
            key = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return !string.IsNullOrWhiteSpace(key);
        }
        catch (FormatException)
        {
            key = string.Empty;
            return false;
        }
    }

    private static ulong ReadVectorDimension(CollectionInfo information)
    {
        if (information.Config?.Params?.VectorsConfig?.Params is { } parameters)
        {
            return parameters.Size;
        }

        throw new InvalidOperationException("Memory storage does not expose a fixed vector dimension.");
    }

    private static string ReadString(Google.Protobuf.Collections.MapField<string, Value> payload, string field)
        => payload.TryGetValue(field, out var value) ? value.StringValue : string.Empty;

    private static bool PayloadEquals(
        Google.Protobuf.Collections.MapField<string, Value> payload,
        string field,
        string expected)
        => payload.TryGetValue(field, out var value)
            && string.Equals(value.StringValue, expected, StringComparison.Ordinal);

    private static double NormalizeScore(float score)
        => Math.Clamp((score + 1d) / 2d, 0d, 1d);
}
