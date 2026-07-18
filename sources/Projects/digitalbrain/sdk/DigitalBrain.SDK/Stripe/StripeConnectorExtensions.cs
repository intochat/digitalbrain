using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.SDK.Stripe;

public static class StripeConnectorExtensions
{
    public static IServiceCollection AddStripeConnector(this IServiceCollection services)
    {
        services.TryAddSingleton<IStripeGateway, StripeGateway>();
        return services;
    }
}
