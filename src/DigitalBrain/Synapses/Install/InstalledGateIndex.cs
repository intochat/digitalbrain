using System.Collections.Concurrent;
using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>
/// An <see cref="IGateIndex"/> that serves only the gates of installed apps: <see cref="SynapseCatalog.GatesOf"/> filtered to the aliases
/// last given to <see cref="Refresh"/> for the brain. A brain never refreshed has no gates. Hosts refresh on <see cref="GateChanged"/>.
/// </summary>
/// <param name="catalog">The live synapse catalog, read on every call.</param>
public sealed class InstalledGateIndex(ISynapseCatalogSource catalog) : IGateIndex
{
    private readonly ConcurrentDictionary<BrainId, HashSet<string>> _installed = new();

    /// <summary>Replaces the installed aliases of <paramref name="brain"/> with those of <paramref name="records"/>.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="records">Its install records.</param>
    public void Refresh(BrainId brain, IReadOnlyList<InstallRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _installed[brain] = new HashSet<string>(records.Select(r => r.Alias), StringComparer.Ordinal);
    }

    /// <summary>The installed aliases last given to <see cref="Refresh"/> for <paramref name="brain"/>; false when the brain was never refreshed.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="aliases">Its installed aliases.</param>
    /// <returns>True when the brain has an entry.</returns>
    public bool TryGetInstalled(BrainId brain, out IReadOnlyCollection<string> aliases)
    {
        var found = _installed.TryGetValue(brain, out var set);
        aliases = found ? set! : [];
        return found;
    }

    // As Refresh, unless the brain was refreshed meanwhile: a load that raced a GateChanged must not overwrite the newer aliases.
    internal void RefreshIfAbsent(BrainId brain, IReadOnlyList<InstallRecord> records) =>
        _ = _installed.TryAdd(brain, new HashSet<string>(records.Select(r => r.Alias), StringComparer.Ordinal));

    /// <summary>Forgets every brain's installed aliases, as a host restart does; the next gate path of a brain loads them again.</summary>
    public void Reset() => _installed.Clear();

    /// <inheritdoc />
    public IReadOnlyList<(NeuronId Gate, SynapseDescriptor Synapse)> GatesFor(BrainId brain, Type signal)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!_installed.TryGetValue(brain, out var aliases)) { return []; }
        return [.. catalog.Current.GatesOf(signal).Where(d => aliases.Contains(d.StateAlias)).Select(d => (new NeuronId(brain, d.StateAlias, d.Key), d))];
    }
}
