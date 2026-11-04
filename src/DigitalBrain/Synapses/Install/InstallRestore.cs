using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>The gate path's restore hook: an installed alias missing from the live catalog (as after a restart) is restored before gates are looked up.</summary>
public static class InstallRestore
{
    /// <summary>Restores every installed alias of <paramref name="brain"/> that <paramref name="catalog"/> does not hold.</summary>
    /// <param name="catalog">The live catalog.</param>
    /// <param name="installs">The installs neuron of the brain.</param>
    /// <param name="restorer">The host's restorer.</param>
    /// <param name="brain">The brain.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>A task that completes when every missing alias was tried.</returns>
    public static async Task EnsureInstalledAliases(ISynapseCatalogSource catalog, IInstalls installs, ISynapseRestorer restorer, BrainId brain, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installs);
        ArgumentNullException.ThrowIfNull(restorer);
        foreach (var record in await installs.List(ct).ConfigureAwait(false))
        {
            if (!catalog.Current.States.ContainsKey(record.Alias))
            {
                _ = await restorer.TryRestore(brain, record.Alias, ct).ConfigureAwait(false);
            }
        }
    }
}
