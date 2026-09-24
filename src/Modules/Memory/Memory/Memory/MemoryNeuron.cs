using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Memory.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Memory;

[GrainType("memory")]
internal sealed class MemoryNeuron(
    TimeProvider time,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MemoryState> state)
    : Neuron, IMemory
{
    private const int MaxRecallLimit = 32;

    private IEmbeddingGenerator<string, Embedding<float>>? _embeddings;
    private IVectorMemoryStore? _store;

    public async Task<MemoryKey> Remember(Remember note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var key = RequireWritableKey(note.Namespace, note.Key);
        if (string.IsNullOrWhiteSpace(note.Text))
        {
            throw new ArgumentException("Provide non-blank text to remember.", nameof(note));
        }

        var (embeddings, store) = RequireDependencies();
        var generated = await embeddings.GenerateAsync([note.Text]).ConfigureAwait(true);
        await store.UpsertAsync(
            new VectorMemoryEntry(this.GetPrimaryKeyString(), key.Namespace, key.Key, note.Text, note.Tags, note.Payload, generated[0].Vector.ToArray()),
            CancellationToken.None).ConfigureAwait(true);

        var current = Current;
        state.State = current with { RememberedCount = current.RememberedCount + 1, LastChangedAt = time.GetUtcNow() };
        await state.WriteStateAsync().ConfigureAwait(true);
        await PublishAsync(new Remembered(key)).ConfigureAwait(true);
        return key;
    }

    public async Task<MemoryKey> Forget(Forget note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var key = RequireWritableKey(note.Namespace, note.Key);
        var (_, store) = RequireDependencies();
        await store.RemoveAsync(this.GetPrimaryKeyString(), key.Namespace, key.Key, CancellationToken.None).ConfigureAwait(true);

        var current = Current;
        state.State = current with { ForgottenCount = current.ForgottenCount + 1, LastChangedAt = time.GetUtcNow() };
        await state.WriteStateAsync().ConfigureAwait(true);
        await PublishAsync(new Forgotten(key)).ConfigureAwait(true);
        return key;
    }

    public async Task<long> PurgeNamespace(PurgeNamespace note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentException.ThrowIfNullOrWhiteSpace(note.Namespace);

        var (_, store) = RequireDependencies();
        var removed = await store.RemoveNamespaceAsync(this.GetPrimaryKeyString(), note.Namespace, CancellationToken.None).ConfigureAwait(true);

        var current = Current;
        state.State = current with { ForgottenCount = current.ForgottenCount + (int)Math.Min(removed, int.MaxValue), LastChangedAt = time.GetUtcNow() };
        await state.WriteStateAsync().ConfigureAwait(true);
        await PublishAsync(new NamespacePurged(note.Namespace, removed)).ConfigureAwait(true);
        return removed;
    }

    [ReadOnly]
    public async Task<RecallResult> Recall(Recall query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Query);
        if (query.Limit is < 1 or > MaxRecallLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Limit), query.Limit, $"Recall limit must be between 1 and {MaxRecallLimit}.");
        }

        var (embeddings, store) = RequireDependencies();
        var metadataFilter = ToMetadataFilter(query.Tags);
        var generated = await embeddings.GenerateAsync([query.Query]).ConfigureAwait(true);
        var matches = await store.SearchAsync(
            this.GetPrimaryKeyString(),
            query.Namespace,
            generated[0].Vector.ToArray(),
            query.Limit,
            metadataFilter,
            CancellationToken.None).ConfigureAwait(true);
        return new RecallResult(matches);
    }

    private MemoryState Current => state.State ?? new MemoryState(0, 0, DateTimeOffset.UnixEpoch);

    private static MemoryKey RequireWritableKey(string @namespace, string key)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new ArgumentException("Provide a non-blank namespace for the note.", nameof(@namespace));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Provide a non-blank key for the note.", nameof(key));
        }

        return new MemoryKey(@namespace, key);
    }

    private (IEmbeddingGenerator<string, Embedding<float>> Embeddings, IVectorMemoryStore Store) RequireDependencies()
    {
        _embeddings ??= ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _store ??= ServiceProvider.GetService<IVectorMemoryStore>();
        if (_embeddings is null || _store is null)
        {
            throw new InvalidOperationException(
                $"Memory neuron '{this.GetPrimaryKeyString()}' is missing an IEmbeddingGenerator or vector store; wire both before retrying.");
        }

        return (_embeddings, _store);
    }

    private static Dictionary<string, string> ToMetadataFilter(IReadOnlyList<MemoryTag> tags)
    {
        var filter = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (!filter.TryAdd(tag.Name, tag.Value))
            {
                throw new ArgumentException($"Duplicate tag name '{tag.Name}'; pass each tag name once.", nameof(tags));
            }
        }

        return filter;
    }
}