using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Abstractions.Bundles;

// Boot-time domain service that installs the declared bundle set onto the substrate through one
// uniform path. The Kernel depends on this abstraction, not on any concrete bundle.
public interface IBundleInstaller
{
    IReadOnlyList<BundleInstallation> InstallDeclared(
        IBundleSource source,
        KernelBundleOptions options,
        IServiceCollection services);

    IReadOnlyList<BundleInstallation> InstallDeclared(
        IReadOnlyList<IBundle> available,
        KernelBundleOptions options,
        IServiceCollection services);
}

public sealed record BundleInstallation(BundleId BundleId, bool Installed, string? Diagnostic = null);
