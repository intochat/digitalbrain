using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Platform.Capacity;

public static class CapacityServiceCollectionExtensions
{
    // The brain registers this facet once, independently of module composition.
    public static IServiceCollection AddCapacity(this IServiceCollection services)
    {
        services.TryAddSingleton<ICapacity, CapacityResolver>();
        return services;
    }
}
