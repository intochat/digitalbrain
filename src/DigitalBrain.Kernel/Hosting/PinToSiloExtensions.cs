using Microsoft.Extensions.DependencyInjection;
using Orleans.Placement;

namespace DigitalBrain.Kernel;

internal static class PinToSiloExtensions
{
    internal static IServiceCollection AddPinToSiloPlacement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddPlacementFilter<PinToSiloStrategy, PinToSiloDirector>(ServiceLifetime.Transient);
    }
}
