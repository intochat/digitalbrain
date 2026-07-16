using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;

namespace DigitalBrain.Mcp;

public interface IOwnerConnectionCatalogClient
{
    Task<IReadOnlyList<OwnerConnectionSnapshot>> ReadAsync(
        BrainOwnerId ownerId,
        CancellationToken cancellationToken = default);
}

public sealed class OwnerConnectionCatalogClient(IClusterClient cluster) : IOwnerConnectionCatalogClient
{
    public async Task<IReadOnlyList<OwnerConnectionSnapshot>> ReadAsync(
        BrainOwnerId ownerId,
        CancellationToken cancellationToken = default) =>
        await cluster.GetGrain<IOwnerConnectionCatalogGrain>(ownerId.Value)
            .ReadAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
}

public static class OwnerConnectionCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddOwnerConnectionCatalogClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOwnerConnectionCatalogClient, OwnerConnectionCatalogClient>();
        return services;
    }
}
