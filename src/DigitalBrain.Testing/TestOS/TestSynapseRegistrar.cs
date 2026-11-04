using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// The TestOS registrar: the live catalog, the state neuron classes and the hosts of the aliases. Signals need no registration: the
// TestOS journal keeps typed objects, and delivery and ObservedSynapse read [Signal] aliases from the type by reflection.
internal sealed class TestSynapseRegistrar(TestOS os) : ISynapseRegistrar
{
    public async Task Activate(BrainId brain, LoadedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();
        if (source.Catalog.States.Keys.FirstOrDefault() is { } first) { os.Source.Replace(first, source.Catalog); }
        else { os.Source.Merge(source.Catalog); }
        foreach (var (alias, record) in source.Catalog.States)
        {
            os.ReplaceStateType(alias, record);
            await os.DropHosts(alias).ConfigureAwait(false);
        }
    }

    public async Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ct.ThrowIfCancellationRequested();
        foreach (var alias in aliases)
        {
            _ = os.Unregister(alias);
            await os.DropHosts(alias).ConfigureAwait(false);
        }
    }
}
