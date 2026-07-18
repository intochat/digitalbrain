using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Comms.Core.Contracts.Messaging;
using TripRadar.Server.Domain.Events;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Emails;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureCoreInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<ILanguageResolver, LanguageResolver>();
        services.AddScoped<ILocalizationValidatorService, LocalizationValidatorService>();
        services.AddScoped<ITranslationService, TranslationService>();
        services.AddScoped<IPreferenceService, PreferenceService>();
        services.AddScoped<IProducerService, ProducerService>();
        services.AddSingleton<IBlindIndexService, BlindIndexService>();
        services.AddScoped<IClientIpResolver, ClientIpResolver>();
        services.AddScoped<IPreferenceMappingService, PreferenceMappingService>();
        services.AddScoped<IRefreshTokenHasher, RefreshTokenHasher>();
        services.AddScoped<IRecoveryTokenHasher, RecoveryTokenHasher>();
        services.AddScoped<IInternalTokenService, InternalTokenService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IUsageEventWriter, UsageEventWriter>();
        services.AddScoped<IUsageSourceResolver, UsageSourceResolver>();
        services.AddSingleton<ICityTranslationProvider, CityTranslationProvider>();
        services.AddScoped<IAirportValidationService, AirportValidationService>();
        services.AddScoped<IEmailTemplateGeneratorService, EmailTemplateGeneratorService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IRecentSearchPayloadBuilder, FlightRecentSearchPayloadBuilder>();
        services.AddScoped<IRecentSearchPayloadBuilder, HotelRecentSearchPayloadBuilder>();
        services.AddScoped<IRecentTripSearchQueryService, RecentTripSearchQueryService>();
        services.AddScoped<ITripVaultQuerySaver, TripVaultQuerySaver>();

        if (environment.IsEnvironment("Test") || environment.IsEnvironment("Testing"))
        {
            services.AddScoped<IDistributedLockService, LocalDistributedLockService>();
            return services;
        }

        var connectionString = configuration.GetConnectionString("db")
            ?? throw new InvalidOperationException("Connection string 'db' is required.");

        services.AddScoped<IDistributedLockService>(serviceProvider => new PostgresDistributedLockService(connectionString,
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgresDistributedLockService>>()));
        return services;
    }
}
