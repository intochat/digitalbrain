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
    private readonly SynapseHost? host;
    private readonly ConcurrentDictionary<(BrainId Brain, string SourceKey), Lazy<Task<bool>>> restoring = new();

    /// <summary>Creates a restorer.</summary>
    /// <param name="loader">Loads the stored builds.</param>
    /// <param name="registrar">Makes a restored source live.</param>
    /// <param name="store">Where the deployed sources are kept.</param>
    /// <param name="host">The synapse host that adopts a restored source as live, so it can update and retire it; optional.</param>
    public SynapseRestorer(SynapseLoader loader, ISynapseRegistrar registrar, ISourceStore store, SynapseHost? host = null)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        this.registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.host = host;
    }

    /// <inheritdoc />
    public async Task<bool> TryRestore(BrainId brain, string alias, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alias);
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
        var build = new SynapseBuild(true, [], record.Assembly, record.Symbols, record.Hash, record.AssemblyName);
        var loaded = loader.Load(build);
        await registrar.Activate(brain, loaded, CancellationToken.None).ConfigureAwait(false);
        if (host is not null)
        {
            _ = await host.Adopt(brain, loaded, record, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }
}
