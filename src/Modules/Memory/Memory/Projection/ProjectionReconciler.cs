using Microsoft.Extensions.AI;

namespace DigitalBrain.Memory;

public sealed class ProjectionReconciler
{
    private readonly IVectorMemoryStore _store;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;

    internal ProjectionReconciler(
        IVectorMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
    }

    public async Task<VectorProjectionReconciled> ReconcileAsync(
        string owner,
        VectorMemoryNamespace @namespace,
        IReadOnlyList<VectorProjectionEntry> desired,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(desired);
        if (!IsReserved(@namespace))
        {
            throw new ArgumentException(
                $"Projection reconcile only accepts reserved namespaces; received '{@namespace.Value}'.",
                nameof(@namespace));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var desiredByKey = new Dictionary<string, VectorProjectionEntry>(StringComparer.Ordinal);
        foreach (var entry in desired)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Text);
            ArgumentNullException.ThrowIfNull(entry.Metadata);
            desiredByKey[entry.Key] = entry with
            {
                Metadata = new Dictionary<string, string>(entry.Metadata, StringComparer.Ordinal),
            };
        }

        var existingKeys = await _store.ListKeysAsync(owner, @namespace.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = existingKeys.ToHashSet(StringComparer.Ordinal);

        var upserted = 0;
        foreach (var entry in desiredByKey.Values.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await EmbedAsync(entry.Text, cancellationToken).ConfigureAwait(false);
            await _store.UpsertAsync(
                    new VectorMemoryEntry(
                        owner,
                        @namespace.Value,
                        entry.Key,
                        entry.Text,
                        entry.Metadata,
                        Payload: null,
                        embedding),
                    cancellationToken)
                .ConfigureAwait(false);
            upserted++;
        }

        var removed = 0;
        foreach (var key in existing.Order(StringComparer.Ordinal))
        {
            if (desiredByKey.ContainsKey(key))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (await _store.RemoveAsync(owner, @namespace.Value, key, cancellationToken).ConfigureAwait(false))
            {
                removed++;
            }
        }

        var activeKeys = desiredByKey.Keys.Order(StringComparer.Ordinal).ToArray();
        return new VectorProjectionReconciled(@namespace, upserted, removed, activeKeys);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await _embeddings.GenerateAsync([text], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return generated[0].Vector.ToArray();
    }

    private static bool IsReserved(VectorMemoryNamespace ns)
        => ns == VectorMemoryNamespace.Capabilities;
}
