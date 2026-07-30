using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Flutter.Http;

internal static class FlutterHttpServices
{
    public static IServiceCollection AddFlutterHttpServices(this IServiceCollection services)
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
