using DigitalBrain.Abstractions.Bundles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace DigitalBrain.Kernel.Hosting;

/// <summary>
/// Stub for iteration build/demo (full implementation port in continuation). Relies on DigitalBrain.Abstractions.
/// </summary>
public static class KernelHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainKernel(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(KernelBundleOptions.Default());
        // TODO: Add full UseOrleans, bundle install, etc from root after more extraction.
        return builder;
    }

    public static IReadOnlyList<BundleInstallation> InstallDigitalBrainDomain(this IServiceCollection services, KernelBundleOptions options)
    {
        return System.Array.Empty<BundleInstallation>();
    }
}
