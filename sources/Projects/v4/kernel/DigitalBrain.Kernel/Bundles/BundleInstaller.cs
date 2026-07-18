using DigitalBrain.Abstractions.Bundles;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Bundles;

// Installs the bundles declared in KernelBundleOptions through one uniform path. Candidate bundles come
// from an IBundleSource; which of them actually install is decided by configuration, never by hardcoded
// type references.
public sealed class BundleInstaller : IBundleInstaller
{
    public IReadOnlyList<BundleInstallation> InstallDeclared(
        IBundleSource source,
        KernelBundleOptions options,
        IServiceCollection services) =>
        InstallDeclared(source.LoadBundles(), options, services);

    public IReadOnlyList<BundleInstallation> InstallDeclared(
        IReadOnlyList<IBundle> available,
        KernelBundleOptions options,
        IServiceCollection services)
    {
        Console.WriteLine($"[BundleInstaller] Discovered {available.Count} bundles: {string.Join(", ", available.Select(b => b.Id.Value))}");
        Console.WriteLine($"[BundleInstaller] Requested bundles to install: {string.Join(", ", options.InstalledBundleIds().Select(b => b.Value))}");

        var byId = available
            .GroupBy(bundle => bundle.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var installations = new List<BundleInstallation>();
        foreach (var bundleId in options.InstalledBundleIds())
        {
            Console.WriteLine($"[BundleInstaller] Attempting to install bundle: {bundleId.Value}");
            if (byId.TryGetValue(bundleId, out var bundle))
            {
                Console.WriteLine($"[BundleInstaller] MATCHED bundle: {bundle.Id.Value} (Type: {bundle.GetType().FullName})");
                if (services.Any(service => IsInstalledMarker(service, bundleId)))
                {
                    Console.WriteLine($"[BundleInstaller] Bundle {bundleId.Value} already installed.");
                    installations.Add(new BundleInstallation(bundleId, true, "Bundle already installed."));
                    continue;
                }

                Console.WriteLine($"[BundleInstaller] Invoking Install on: {bundle.Id.Value}");
                bundle.Install(services);
                services.AddSingleton(new InstalledBundleMarker(bundleId));
                installations.Add(new BundleInstallation(bundleId, true));
            }
            else
            {
                Console.WriteLine($"[BundleInstaller] FAILED to find bundle: {bundleId.Value}");
                installations.Add(new BundleInstallation(
                    bundleId,
                    false,
                    $"No bundle named '{bundleId.Value}' was found among the installed assemblies."));
            }
        }

        return installations;
    }

    public static IReadOnlyList<IBundle> DiscoverFromBaseDirectory()
        => new LocalDiskBundleSource().LoadBundles();

    private static bool IsInstalledMarker(ServiceDescriptor service, BundleId bundleId) =>
        service.ImplementationInstance is InstalledBundleMarker marker &&
        marker.BundleId == bundleId;

    private sealed record InstalledBundleMarker(BundleId BundleId);
}
