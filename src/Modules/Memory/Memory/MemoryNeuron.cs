using System.Text.Json;
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
internal sealed class MemoryNeuron : Neuron<MemoryState>, IMemory
{
    private const string ReservedNamespace = "digitalbrain.capabilities";
    private const int MaxRecallLimit = 32;

    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddings;
    private readonly IVectorMemoryStore? _store;

    public MemoryNeuron(
        NeuronRuntime runtime,
        [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MemoryState> state)
        : base(runtime, state)
    {
        _embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _store = ServiceProvider.GetService<IVectorMemoryStore>();
    }

    public Task<Accepted<MemoryKey>> Remember(Remember command) => ExecuteCommandAsync(
        Descriptor("remember"), command, MemoryJson.Default.Remember, MemoryJson.Default.AcceptedMemoryKey, arguments =>
        {
            var key = RequireWritableKey(arguments.Id, arguments.Namespace, arguments.Key);

            if (string.IsNullOrWhiteSpace(arguments.Text))
            {
                throw new CommandRejectedException(arguments.Id, "text is blank", "Provide non-blank text to remember.");
            }

            var body = new RememberingBody(arguments.Namespace, arguments.Key, arguments.Text, arguments.Tags, arguments.Payload);
            var work = Schedule(Signal.Create(MemorySignals.Remembering, JsonSerializer.Serialize(body, MemoryJson.Default.RememberingBody)));
            return new Accepted<MemoryKey>(key, work);
        });

    public Task<Accepted<MemoryKey>> Forget(Forget command) => ExecuteCommandAsync(
        Descriptor("forget"), command, MemoryJson.Default.Forget, MemoryJson.Default.AcceptedMemoryKey, arguments =>
        {
            var key = RequireWritableKey(arguments.Id, arguments.Namespace, arguments.Key);
            var work = Schedule(Signal.Create(MemorySignals.Forgetting, JsonSerializer.Serialize(key, MemoryJson.Default.MemoryKey)));
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
        switch (delivery.Signal.Type)
        {
            case MemorySignals.Remembering:
                {
                    var body = JsonSerializer.Deserialize(delivery.Signal.Body, MemoryJson.Default.RememberingBody)!;
                    var (embeddings, store) = RequireDependencies();
                    var generated = await embeddings.GenerateAsync([body.Text], cancellationToken: cancellationToken).ConfigureAwait(true);
                    await store.UpsertAsync(new VectorMemoryEntry(Id.Name, body.Namespace, body.Key, body.Text, body.Tags,
                        body.Payload, generated[0].Vector.ToArray()), cancellationToken).ConfigureAwait(true);
                    await SaveAsync(new MemoryState((State?.RememberedCount ?? 0) + 1, State?.ForgottenCount ?? 0,
                        TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
                    var key = new MemoryKey(body.Namespace, body.Key);
                    await FireAsync(Signal.Create(MemorySignals.Remembered, JsonSerializer.Serialize(key, MemoryJson.Default.MemoryKey)),
                        to: null, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case MemorySignals.Forgetting:
                {
                    var key = JsonSerializer.Deserialize(delivery.Signal.Body, MemoryJson.Default.MemoryKey)!;
                    var (_, store) = RequireDependencies();
                    await store.RemoveAsync(Id.Name, key.Namespace, key.Key, cancellationToken).ConfigureAwait(true);
                    await SaveAsync(new MemoryState(State?.RememberedCount ?? 0, (State?.ForgottenCount ?? 0) + 1,
                        TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
                    await FireAsync(Signal.Create(MemorySignals.Forgotten, JsonSerializer.Serialize(key, MemoryJson.Default.MemoryKey)),
                        to: null, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
                    break;
                }
        }
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
        if (_embeddings is null || _store is null)
        {
            throw new InvalidOperationException(
                $"Memory neuron '{Id}' is missing an IEmbeddingGenerator or vector store; wire an IEmbeddingGenerator (an embedding model in the AppHost) and a vector store before retrying.");
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
