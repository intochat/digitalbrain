using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.UI;

internal static class UiHttpServices
{
    public static IServiceCollection AddUiHttpServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            OwnerSessionJournal.Open(
                services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IDigitalBrain>().Owner));
        services.TryAddSingleton<BrainTopologyReader>();

        return services;
    }
}
