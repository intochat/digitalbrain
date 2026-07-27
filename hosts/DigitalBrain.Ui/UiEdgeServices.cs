using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Ui;

internal static class UiEdgeServices
{
    public static IServiceCollection AddUiEdgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            OwnerSessionJournal.Open(
                services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IDigitalBrain>().Owner));

        return services;
    }
}
