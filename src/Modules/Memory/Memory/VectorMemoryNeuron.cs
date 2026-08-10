using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Memory;

[GrainType("vectormemory")]
public sealed class VectorMemoryNeuron :
    Neuron,
    IVectorMemory,
    IHandle<StoreVectorMemory>,
    IHandle<SearchVectorMemory>,
    IHandle<RemoveVectorMemory>
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
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var embedding = await EmbedAsync(synapse.Text, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = synapse.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(synapse.Metadata, StringComparer.Ordinal);

        await _store.UpsertAsync(
            new VectorMemoryEntry(
                Id.Owner.Value,
                synapse.Namespace.Value,
                synapse.Key,
                synapse.Text,
                metadata,
                synapse.Payload,
                embedding),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(
            new VectorMemoryStored(
                Stored: true,
                synapse.Namespace,
                synapse.Key,
                VectorMemoryStoreStatus.Stored),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(SearchVectorMemory synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSearch(synapse);

        var queryEmbedding = await EmbedAsync(synapse.Query, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = await _store.SearchAsync(
            Id.Owner.Value,
            synapse.Namespace.Value,
            queryEmbedding,
            synapse.Limit,
            synapse.Metadata,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(new VectorMemoryMatches(synapse.Namespace, matches), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(RemoveVectorMemory synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRemove(synapse);

        if (IsReserved(synapse.Namespace))
        {
            await ReplyAsync(new VectorMemoryRemoved(Removed: false, synapse.Namespace, synapse.Key), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var removed = await _store.RemoveAsync(
            Id.Owner.Value,
            synapse.Namespace.Value,
            synapse.Key,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(new VectorMemoryRemoved(removed, synapse.Namespace, synapse.Key), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await _embeddings.GenerateAsync(
            [text],
            cancellationToken: cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return generated[0].Vector.ToArray();
    }

    private static bool IsReserved(VectorMemoryNamespace ns) =>
        ns == VectorMemoryNamespace.Capabilities;

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
