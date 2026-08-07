using DigitalBrain.Client;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.OS.UiEdge;

internal static class UiEdgeServices
{
    public static IServiceCollection AddUiEdgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            new OwnerSessionJournal(services.GetRequiredService<IDigitalBrain>()));

        return services;
    }
}
