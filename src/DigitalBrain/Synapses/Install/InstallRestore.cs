namespace DigitalBrain.Units;

/// <summary>The install restore hook: an installed alias missing from the live catalog (as after a restart) is restored.</summary>
public static class InstallRestore
{
    /// <summary>Restores every installed alias of <paramref name="brain"/> that <paramref name="catalog"/> does not hold.</summary>
    /// <param name="catalog">The live catalog.</param>
    /// <param name="installed">The brain's installed aliases.</param>
    /// <param name="restorer">The host's restorer.</param>
    /// <param name="brain">The brain.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>A task that completes when every missing alias was tried.</returns>
    public static async Task EnsureInstalledAliases(ISynapseCatalogSource catalog, IEnumerable<string> installed, ISynapseRestorer restorer, BrainId brain, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(restorer);
        foreach (var alias in installed.ToList())
        {
            if (!catalog.Current.States.ContainsKey(alias))
            {
                _ = await restorer.TryRestore(brain, alias, ct).ConfigureAwait(false);
            }
        }
    }
}
