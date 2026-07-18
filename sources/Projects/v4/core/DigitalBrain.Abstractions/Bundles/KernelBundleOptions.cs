namespace DigitalBrain.Abstractions.Bundles;

// The set of bundles the Kernel installs at boot, bound from configuration so the installed
// surface is data-driven rather than a hardcoded list of concrete bundle types.
public sealed class KernelBundleOptions
{
    public const string SectionName = "DigitalBrain:Kernel:Bundles";

    public IList<string> Installed { get; init; } = new List<string>();

    public static KernelBundleOptions Default() => new()
    {
        Installed =
        {
            WellKnownBundleIds.Marketplace,
            WellKnownBundleIds.Ino,
            WellKnownBundleIds.Awesome
        }
    };

    public IReadOnlyList<BundleId> InstalledBundleIds() =>
        Installed
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => new BundleId(WellKnownBundleIds.Canonicalize(id)))
            .ToList();
}
