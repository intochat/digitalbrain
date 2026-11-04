using System.Collections.Concurrent;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Restores a generated source from the brain's stored record when a host misses one of its aliases, as after a restart: the stored
/// build is loaded (not compiled again) and made live through the registrar, and the synapse host adopts it as the live version.
/// Concurrent misses of the same brain and source share one restore.
/// </summary>
public sealed class SynapseRestorer : ISynapseRestorer
{
    private readonly SynapseLoader loader;
    private readonly ISynapseRegistrar registrar;
    private readonly ISourceStore store;
    private readonly ISynapseCatalogSource catalog;
    private readonly SynapseHost? host;
    private readonly ConcurrentDictionary<(BrainId Brain, string SourceKey), Lazy<Task<bool>>> restoring = new();

    /// <summary>Creates a restorer.</summary>
    /// <param name="loader">Loads the stored builds.</param>
    /// <param name="registrar">Makes a restored source live.</param>
    /// <param name="store">Where the deployed sources are kept.</param>
    /// <param name="catalog">The live catalog: an alias it holds is already live.</param>
    /// <param name="host">The synapse host that adopts a restored source as live, so it can update and retire it; optional.</param>
    public SynapseRestorer(SynapseLoader loader, ISynapseRegistrar registrar, ISourceStore store, ISynapseCatalogSource catalog, SynapseHost? host = null)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.host = host;
    }

    /// <inheritdoc />
    public async Task<bool> TryRestore(BrainId brain, string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (catalog.Current.States.ContainsKey(alias))
        {
            return true;
        }

        var records = await store.List(brain, ct).ConfigureAwait(false);
        if (records.FirstOrDefault(r => r.Aliases.Contains(alias, StringComparer.Ordinal)) is not { } record)
        {
            return false;
        }

        var key = (brain, record.SourceKey);
        var restore = restoring.GetOrAdd(key, _ => new Lazy<Task<bool>>(() => Restore(brain, record)));
        try
        {
            return await restore.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Only a restore in flight is shared: once done the alias is registered, and a later miss (another restart) restores again.
            _ = restoring.TryRemove(new KeyValuePair<(BrainId Brain, string SourceKey), Lazy<Task<bool>>>(key, restore));
        }
    }

    private async Task<bool> Restore(BrainId brain, SourceRecord record)
    {
        // A context of the stored build still live in this process (an earlier restore) is reused: the loader loads a pair once.
        var reused = loader.TryGetLive(record.SourceKey, record.Hash, out var live);
        var loaded = reused ? live! : loader.Load(new SynapseBuild(true, [], record.Assembly, record.Symbols, record.Hash, record.AssemblyName));
        try
        {
            await registrar.Activate(brain, loaded, CancellationToken.None).ConfigureAwait(false);
            if (host is not null)
            {
                _ = await host.Adopt(brain, loaded, record, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch when (!reused)
        {
            // The new context is unloaded so a retry can load the build again.
            _ = loader.Unload(loaded);
            throw;
        }

        return true;
    }
}
