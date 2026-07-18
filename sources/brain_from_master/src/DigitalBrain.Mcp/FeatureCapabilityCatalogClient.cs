using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;

namespace DigitalBrain.Mcp;

public interface IFeatureCapabilityCatalog
{
    Task<IReadOnlyList<CapabilityDescriptor>> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed class FeatureCapabilityCatalogClient(IClusterClient cluster) : IFeatureCapabilityCatalog
{
    public async Task<IReadOnlyList<CapabilityDescriptor>> ReadAsync(CancellationToken cancellationToken = default) =>
        await cluster.GetGrain<ICapabilityCatalogProjectionGrain>(CapabilityCatalogProjectionGrainIds.Singleton)
            .ReadAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
}

public static class FeatureCapabilityCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureCapabilityCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFeatureCapabilityCatalog, FeatureCapabilityCatalogClient>();
        return services;
    }
}
