using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Ui;

internal static class UIEdgeServices
{
    public static IServiceCollection AddUIEdgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            OwnerSessionJournal.Open(
                services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IDigitalBrain>().Owner));

        return services;
    }
}
