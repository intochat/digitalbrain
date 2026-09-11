using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Memory;

[GrainType("memory")]
internal sealed class MemoryNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<MemoryState>> state)
    : Neuron<MemoryState>(runtime, state), IMemory
{
    private const string ReservedNamespace = "digitalbrain.capabilities";
    private const int MaxRecallLimit = 32;

    private Lazy<IEmbeddingGenerator<string, Embedding<float>>?>? _embeddings;
    private Lazy<IVectorMemoryStore?>? _store;

    public Task<Accepted<MemoryKey>> Remember(Remember command) => ExecuteCommandAsync(
        Descriptor("remember"), command, MemoryJson.Default.Remember, MemoryJson.Default.AcceptedMemoryKey, arguments =>
        {
            var key = RequireWritableKey(arguments.Id, arguments.Namespace, arguments.Key);

            if (string.IsNullOrWhiteSpace(arguments.Text))
            {
                throw new CommandRejectedException(arguments.Id, "text is blank", "Provide non-blank text to remember.");
            }

            var body = new RememberingBody(arguments.Namespace, arguments.Key, arguments.Text, arguments.Tags, arguments.Payload);
            var work = Schedule(Signal.FromJson(MemorySignals.MemoryRemembering, body, MemoryJson.Default.RememberingBody));
            return new Accepted<MemoryKey>(key, work);
        });

    public Task<Accepted<MemoryKey>> Forget(Forget command) => ExecuteCommandAsync(
        Descriptor("forget"), command, MemoryJson.Default.Forget, MemoryJson.Default.AcceptedMemoryKey, arguments =>
        {
            var key = RequireWritableKey(arguments.Id, arguments.Namespace, arguments.Key);
            var work = Schedule(Signal.FromJson(MemorySignals.MemoryForgetting, key, MemoryJson.Default.MemoryKey));
            return new Accepted<MemoryKey>(key, work);
        });

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
        var matches = await store.SearchAsync(Id.Name, query.Namespace, generated[0].Vector.ToArray(), query.Limit,
            metadataFilter, CancellationToken.None).ConfigureAwait(true);
        return new RecallResult(matches);
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        MemoryState next;
        switch (delivery.Signal.Type)
        {
            case MemorySignals.MemoryRemembering:
                {
                    if (Body(delivery, MemoryJson.Default.RememberingBody) is not { } body)
                    {
                        return;
                    }

                    var (embeddings, store) = RequireDependencies();
                    var generated = await embeddings.GenerateAsync([body.Text], cancellationToken: cancellationToken).ConfigureAwait(true);
                    await store.UpsertAsync(new VectorMemoryEntry(Id.Name, body.Namespace, body.Key, body.Text, body.Tags,
                        body.Payload, generated[0].Vector.ToArray()), cancellationToken).ConfigureAwait(true);
                    next = new MemoryState((State?.RememberedCount ?? 0) + 1, State?.ForgottenCount ?? 0,
                        TimeProvider.GetUtcNow());
                    var key = new MemoryKey(body.Namespace, body.Key);
                    Announce(Signal.FromJson(MemorySignals.Remembered, key, MemoryJson.Default.MemoryKey));
                    break;
                }
            case MemorySignals.MemoryForgetting:
                {
                    if (Body(delivery, MemoryJson.Default.MemoryKey) is not { } key)
                    {
                        return;
                    }

                    var (_, store) = RequireDependencies();
                    await store.RemoveAsync(Id.Name, key.Namespace, key.Key, cancellationToken).ConfigureAwait(true);
                    next = new MemoryState(State?.RememberedCount ?? 0, (State?.ForgottenCount ?? 0) + 1,
                        TimeProvider.GetUtcNow());
                    Announce(Signal.FromJson(MemorySignals.Forgotten, key, MemoryJson.Default.MemoryKey));
                    break;
                }
            default:
                return;
        }

        await SaveAsync(next, cancellationToken).ConfigureAwait(true);
    }

    private static MemoryKey RequireWritableKey(CommandId id, string @namespace, string key)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new CommandRejectedException(id, "namespace is blank", "Provide a non-blank namespace for the note.");
        }

        if (@namespace == ReservedNamespace)
        {
            throw new CommandRejectedException(id, "namespace is reserved", "Choose a namespace other than digitalbrain.capabilities.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new CommandRejectedException(id, "key is blank", "Provide a non-blank key for the note.");
        }

        return new MemoryKey(@namespace, key);
    }

    private (IEmbeddingGenerator<string, Embedding<float>> Embeddings, IVectorMemoryStore Store) RequireDependencies()
    {
        var embeddings = (_embeddings ??= new(() => ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>())).Value;
        var store = (_store ??= new(() => ServiceProvider.GetService<IVectorMemoryStore>())).Value;
        if (embeddings is null || store is null)
        {
            throw new InvalidOperationException(
                $"Memory neuron '{Id}' is missing an IEmbeddingGenerator or vector store; wire an IEmbeddingGenerator (an embedding model in the AppHost) and a vector store before retrying.");
        }

        return (embeddings, store);
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
