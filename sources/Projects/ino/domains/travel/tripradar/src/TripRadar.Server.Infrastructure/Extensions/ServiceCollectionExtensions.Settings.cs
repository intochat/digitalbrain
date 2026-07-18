using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Infrastructure.Providers.Kiwi.Settings;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;
using TripRadar.Server.Infrastructure.Providers.Stripe.Settings;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureSettings(this IServiceCollection services, IConfiguration configuration) =>
        services
            .Configure<SerpApiSettings>(configuration.GetSection("SerpApiSettings"))
            .Configure<KiwiCalendarSettings>(configuration.GetSection("KiwiCalendarSettings"))
            .Configure<PaymentSettings>(configuration.GetSection("PaymentSettings"))
            .Configure<StripeApiSettings>(configuration.GetSection("PaymentSettings:Stripe"))
            .Configure<Kafka>(configuration.GetSection("Kafka"))
            .Configure<MockApi>(configuration.GetSection("MockApi"))
            .Configure<EmailSettings>(configuration.GetSection("EmailSettings"))
            .Configure<JobSettings>(configuration.GetSection("JobSettings"))
            .Configure<TelegramSettings>(configuration.GetSection("TelegramSettings"))
            .Configure<ResiliencePolicySettings>(configuration.GetSection("ResiliencePolicySettings"))
            .Configure<EncryptionSettings>(configuration.GetSection("Encryption"))
            .Configure<App>(configuration.GetSection("App"))
            .Configure<CachingSettings>(configuration.GetSection("Caching"))
            .Configure<Jwt>(configuration.GetSection("Jwt"))
            .Configure<MockApi>(configuration.GetSection("MockApi"));
}
