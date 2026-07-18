using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    public static void ConfigureInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        EncryptionExtensions.Configure(configuration["Encryption:UserDataKey"] ?? throw new InvalidOperationException("Encryption:UserDataKey is required."));

        services
            .ConfigureDatabase(configuration, environment)
            .ConfigureRepositories()
            .ConfigureAutomapper()
            .ConfigureSettings(configuration)
            .ConfigureCoreInfrastructure(configuration, environment)
            .ConfigureAuthenticationInfrastructure()
            .ConfigureExternalServiceProviders(configuration, environment)
            .ConfigureSchedulingInfrastructure(configuration, environment)
            .ConfigureBackgroundJobsInfrastructure(configuration, environment)
            .ConfigurePaymentServices();
    }
}
