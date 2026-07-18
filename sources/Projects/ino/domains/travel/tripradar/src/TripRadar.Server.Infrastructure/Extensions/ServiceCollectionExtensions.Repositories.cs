using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Repositories;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring repository services.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures all repository services.
    /// </summary>
    private static IServiceCollection ConfigureRepositories(this IServiceCollection services) =>
        services
            .AddScoped(typeof(IRepository<>), typeof(Repository<>))
            .AddScoped<IUserPreferencesRepository, UserPreferencesRepository>()
            .AddScoped<IScheduledFlightQueryRepository, ScheduledFlightQueryRepository>()
            .AddScoped<IScheduledHotelQueryRepository, ScheduledHotelQueryRepository>()
            .AddScoped<IScheduledLocalPlacesQueryRepository, ScheduledLocalPlacesQueryRepository>()
            .AddScoped<IUserRepository, UserRepository>()
            .AddScoped<IAirportRepository, AirportRepository>()
            .AddScoped<ICountryRepository, CountryRepository>()
            .AddScoped<IDomainRepository, DomainRepository>()
            .AddScoped<ITripAdvisorDomainRepository, TripAdvisorDomainRepository>()
            .AddScoped<IOpenTableDomainRepository, OpenTableDomainRepository>()
            .AddScoped<IYelpDomainRepository, YelpDomainRepository>()
            .AddScoped<IYelpReviewLanguageRepository, YelpReviewLanguageRepository>()
            .AddScoped<IGoogleLrLanguageRepository, GoogleLrLanguageRepository>()
            .AddScoped<IAirlineRepository, AirlineRepository>()
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddScoped<ITierRepository, TierRepository>()
            .AddScoped<IScheduledExecutionRepository, ScheduledExecutionRepository>()
            .AddScoped<IUserMonthlyTokenCountRepository, UserMonthlyTokenCountRepository>()
            .AddScoped<IServiceTokenCostRepository, ServiceTokenCostRepository>()
            .AddScoped<ILanguageRepository, LanguageRepository>()
            .AddScoped<ITimezoneRepository, TimezoneRepository>()
            .AddScoped<ICurrencyRepository, CurrencyRepository>()
            .AddScoped<ILocationRepository, LocationRepository>()
            .AddScoped<IScheduledEventQueryRepository, ScheduledEventQueryRepository>()
            .AddScoped<IPriceRepository, PriceRepository>()
            .AddScoped<IFeedbackRepository, FeedbackRepository>()
            .AddScoped<IUserSubscriptionRepository, UserSubscriptionRepository>()
            .AddScoped<IOverageBillingRecordRepository, OverageBillingRecordRepository>()
            .AddScoped<IPreferenceTypeRepository, PreferenceTypeRepository>()
            .AddScoped<IPromoCodeRepository, PromoCodeRepository>()
            .AddScoped<IPromoCodeUsageRepository, PromoCodeUsageRepository>()
            .AddScoped<ITripVaultRepository, TripVaultRepository>()
            .AddScoped<IUsageEventRepository, UsageEventRepository>()
            .AddScoped<IMetterPaymentProcessor, MetterPaymentProcessor>();
}
