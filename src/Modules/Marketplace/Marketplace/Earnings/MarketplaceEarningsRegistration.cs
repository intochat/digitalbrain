using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Marketplace;

// The earnings half of the Marketplace module. The Marketplace module registers this; tests can
// call it directly to override the take rate or swap an adapter.
public static class MarketplaceEarningsRegistration
{
    public static IServiceCollection AddMarketplaceEarnings(
        this IServiceCollection services,
        Action<MarketplaceEarningsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) { services.Configure(configure); }
        services.TryAddSingleton<ISanctionsScreener>(FakeSanctionsScreener.Instance);
        services.TryAddSingleton<ICreatorOnboardingProvider>(provider =>
            new FakeCreatorOnboardingProvider(provider.GetRequiredService<ISanctionsScreener>()));
        services.TryAddSingleton<IPayoutProvider>(FakePayoutProvider.Instance);
        return services;
    }
}
