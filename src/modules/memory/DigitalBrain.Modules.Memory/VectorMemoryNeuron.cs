using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

[GrainType("vectormemory")]
public sealed class VectorMemoryNeuron :
    Neuron,
    IVectorMemory,
    IHandle<StoreVectorMemory>,
    IHandle<SearchVectorMemory>,
    IHandle<RemoveVectorMemory>,
    IEmit<VectorMemoryStored>,
    IEmit<VectorMemoryMatches>,
    IEmit<VectorMemoryRemoved>
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IVectorMemoryStore _store;

    public VectorMemoryNeuron()
    {
        _embeddings = ServiceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _store = ServiceProvider.GetRequiredService<IVectorMemoryStore>();
    }

    public async Task HandleAsync(StoreVectorMemory synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateStore(synapse);

        if (IsReserved(synapse.Namespace))
        {
            await ReplyAsync(
                new VectorMemoryStored(
                    Stored: false,
                    synapse.Namespace,
                    synapse.Key,
                    VectorMemoryStoreStatus.ReservedNamespace),
                cancellationToken);
            return;
        }

        var embedding = await EmbedAsync(synapse.Text, cancellationToken);
        var metadata = synapse.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(synapse.Metadata, StringComparer.Ordinal);

        _store.Upsert(new VectorMemoryEntry(
            Id.Owner.Value,
            synapse.Namespace.Value,
            synapse.Key,
            synapse.Text,
            metadata,
            synapse.Payload,
            embedding));

        await ReplyAsync(
            new VectorMemoryStored(
                Stored: true,
                synapse.Namespace,
                synapse.Key,
                VectorMemoryStoreStatus.Stored),
            cancellationToken);
    }

    public async Task HandleAsync(SearchVectorMemory synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSearch(synapse);

        var queryEmbedding = await EmbedAsync(synapse.Query, cancellationToken);
        var matches = _store.Search(
            Id.Owner.Value,
            synapse.Namespace.Value,
            queryEmbedding,
            synapse.Limit,
            synapse.Metadata);

        await ReplyAsync(new VectorMemoryMatches(synapse.Namespace, matches), cancellationToken);
    }

    public Task HandleAsync(RemoveVectorMemory synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRemove(synapse);

        var removed = _store.Remove(Id.Owner.Value, synapse.Namespace.Value, synapse.Key);
        return ReplyAsync(new VectorMemoryRemoved(removed, synapse.Namespace, synapse.Key), cancellationToken);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await _embeddings.GenerateAsync(
            [text],
            cancellationToken: cancellationToken);

        return generated[0].Vector.ToArray();
    }

    private static bool IsReserved(VectorMemoryNamespace ns) =>
        ns == VectorMemoryNamespace.Capabilities || ns == VectorMemoryNamespace.Behaviors;

    private static void ValidateStore(StoreVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
    }

    private static void ValidateSearch(SearchVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Limit, 1);
    }

    private static void ValidateRemove(RemoveVectorMemory request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);
    }
}
