using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Platform.Capacity;

public static class CapacityServiceCollectionExtensions
{
    // Idempotent: any module needing capacity calls this; the ring-law migration will move the
    // call into the kernel so the facet stands up unconditionally.
    public static IServiceCollection AddCapacity(this IServiceCollection services)
    {
        services.TryAddSingleton<ICapacity, CapacityResolver>();
        return services;
    }
}
