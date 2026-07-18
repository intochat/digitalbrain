using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime;

[GenerateSerializer]
public sealed record CatalogedNeuron(
    [property: Id(0)] NeuronId Id,
    [property: Id(1)] DateTimeOffset FirstSeenAt,
    [property: Id(2)] DateTimeOffset LastSeenAt);

// IsInterpreted distinguishes InoLang neurons (whose Incoming synapse FQNs
// the Navigator must dispatch to IInterpretedNeuronGrain.HandleAsync) from
// native C# IHandle<T> neurons (existing stream path). Appended as the last
// Id with a default so the existing scanner-built positional ctor callsites
// stay source-compatible — Orleans serializer requires no Id reordering for
// backward compatibility with persisted records.
//
// HandledSignalSubscriptions (E-RUN #37) carries the signal contract FQNs the
// neuron subscribes to via `on signal(T):` handlers, parallel to
// HandledSynapseTypes. The Navigator's ResolveSubscribersAsync(signalFqn)
// scans this list (broadcast fan-out — multiple subscribers per FQN are
// expected). Appended as Id(8) on the body (init-only with empty default)
// rather than a positional parameter, so existing 7- and 8-arg positional
// ctor callsites stay source-compatible — a positional collection parameter
// cannot default to Array.Empty<string>() (C# requires a constant default),
// and a nullable positional default would push null-checks into every
// reader. Per the Orleans no-reorder rule, Id(8) is appended after Id(7).
[GenerateSerializer]
public sealed record NeuronCatalogEntry(
    [property: Id(0)] NeuronId Id,
    [property: Id(1)] string Icon,
    [property: Id(2)] NeuronCapability Capabilities,
    [property: Id(3)] string TypeFullName,
    [property: Id(4)] IReadOnlyList<string> CapabilityMarkers,
    [property: Id(5)] IReadOnlyList<string> HandledSynapseTypes,
    [property: Id(6)] string Domain,
    [property: Id(7)] bool IsInterpreted = false)
{
    [Id(8)]
    public IReadOnlyList<string> HandledSignalSubscriptions { get; init; } = Array.Empty<string>();

    [Id(9)]
    public string? UiLayoutJson { get; init; }
}

[GenerateSerializer]
public sealed record SynapseSlice(
    [property: Id(0)] long NextCursor,
    [property: Id(1)] IReadOnlyList<Synapse> Records);

public interface IBrainCatalog : IGrainWithStringKey
{
    Task RegisterAsync(NeuronCatalogEntry entry);
    Task<IReadOnlyList<NeuronCatalogEntry>> ListRegisteredAsync();
    Task<IReadOnlyList<CatalogedNeuron>> ListNeuronsAsync();
    Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since);
    Task<SynapseSlice> WatchSinceAsync(long cursor);
}
