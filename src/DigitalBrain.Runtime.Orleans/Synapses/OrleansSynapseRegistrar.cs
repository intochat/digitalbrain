using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The registrar of the silo. The live catalog, the neuron type registry and the signal type map are process-wide; a replaced or removed
/// alias reloads the brain's state neuron <c>{brain}/{alias}/default</c> through <see cref="INeuronHost.Reload"/>, so its next activation
/// hosts the class registered then. A signal alias is unregistered with the source that last registered it.
/// </summary>
internal sealed class OrleansSynapseRegistrar(
    SynapseCatalogSource catalog, NeuronTypeRegistry types, JournalPayloads payloads, IGrainFactory grains) : ISynapseRegistrar
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _signals = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task Activate(BrainId brain, LoadedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var signal in source.Signals)
            {
                var alias = signal.GetCustomAttributes(typeof(SignalAttribute), false).OfType<SignalAttribute>().Single().Alias;
                if (!payloads.Types.TryResolve(alias, out var held)) { payloads.Types.Register(signal); }
                else if (held != signal) { payloads.Types.Replace(signal); }
                _signals[alias] = source.SourceKey;
            }
        }
        if (source.Catalog.States.Keys.FirstOrDefault() is { } first) { catalog.Replace(first, source.Catalog); }
        else { catalog.Merge(source.Catalog); }
        foreach (var (alias, record) in source.Catalog.States) { types.RegisterState(alias, record); }
        foreach (var alias in source.Catalog.States.Keys) { await Reload(brain, alias).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public async Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(aliases);
        ct.ThrowIfCancellationRequested();
        foreach (var alias in aliases)
        {
            catalog.Remove(alias);
            types.UnregisterState(alias);
        }
        lock (_gate)
        {
            foreach (var alias in _signals.Where(pair => pair.Value == sourceKey).Select(pair => pair.Key).ToList())
            {
                payloads.Types.Unregister(alias);
                _signals.Remove(alias);
            }
        }
        foreach (var alias in aliases) { await Reload(brain, alias).ConfigureAwait(false); }
    }

    // An inactive grain of a removed alias cannot activate to hear the reload, and needs none.
    private async Task Reload(BrainId brain, string alias)
    {
        var host = grains.GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, new NeuronId(brain, alias, "default").ToString()));
        try { await host.Reload().ConfigureAwait(false); }
        catch (BindingException) { }
    }
}
