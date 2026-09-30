using System.Globalization;
using DigitalBrain.Qdrant;

namespace DigitalBrain.Memory;

// The optional Qdrant projection of canonical memory. The payload schema is unchanged from the
// direct-client store, so existing collections keep working.
internal sealed class VectorMemoryStore(IQdrant qdrant, string? collectionName) : IVectorMemoryStore
{
    internal const string DefaultCollectionName = "digitalbrain_vector_memory";

    private const string NameField = "owner";
    private const string NamespaceField = "namespace";
    private const string KeyField = "key";
    private const string TextField = "text";
    private const string MetadataPrefix = "m_";
    private const string PayloadIdField = "payload_id";
    private const string PayloadExpiresField = "payload_expires";

    private readonly string _collection = string.IsNullOrWhiteSpace(collectionName) ? DefaultCollectionName : collectionName;

    public Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NameField] = entry.Name,
            [NamespaceField] = entry.Namespace,
            [KeyField] = entry.Key,
            [TextField] = entry.Text,
        };
        foreach (var tag in entry.Tags) { payload[MetadataPrefix + tag.Name] = tag.Value; }
        if (entry.Payload is { } protectedPayload)
        {
            payload[PayloadIdField] = Guid.Parse(protectedPayload.Id).ToString("D");
            if (protectedPayload.ExpiresAt is { } expiresAt)
            { payload[PayloadExpiresField] = expiresAt.ToString("O", CultureInfo.InvariantCulture); }
        }
        return qdrant.Upsert(_collection, [new(PointKey(entry.Name, entry.Namespace, entry.Key), entry.Embedding, payload)], cancellationToken);
    }

    public async Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken)
    {
        var pointKey = PointKey(name, @namespace, key);
        var payload = await qdrant.ReadPayload(_collection, pointKey, cancellationToken).ConfigureAwait(false);
        if (payload is null || !Matches(payload, NameField, name) || !Matches(payload, NamespaceField, @namespace) || !Matches(payload, KeyField, key))
        { return false; }
        await qdrant.Delete(_collection, [pointKey], cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<long> RemoveNamespaceAsync(string name, string @namespace, CancellationToken cancellationToken)
        => qdrant.DeleteWhere(_collection, Scope(name, @namespace), cancellationToken);

    private static string PointKey(string name, string @namespace, string key) => name + "\0" + @namespace + "\0" + key;

    private static Dictionary<string, string> Scope(string name, string @namespace)
        => new(StringComparer.Ordinal) { [NameField] = name, [NamespaceField] = @namespace };

    private static bool Matches(IReadOnlyDictionary<string, string> payload, string field, string expected)
        => payload.TryGetValue(field, out var value) && string.Equals(value, expected, StringComparison.Ordinal);
}
