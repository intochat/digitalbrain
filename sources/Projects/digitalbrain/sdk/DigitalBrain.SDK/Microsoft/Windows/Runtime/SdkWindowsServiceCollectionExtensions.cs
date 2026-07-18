using DigitalBrain.SDK.Microsoft.Windows.FileSystem;

namespace DigitalBrain.SDK.Microsoft.Windows.Runtime;

public static class SdkWindowsServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalBrainSdkWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Force-load every grain type in this assembly before the AssemblyScanningContractCatalog
        // builds — touching the type triggers JIT / class-loader before reflection scans.
        GC.KeepAlive(typeof(WindowsRuntimeNeuronGrain));
        GC.KeepAlive(typeof(WindowsFileSystem));

        return services;
    }
}
