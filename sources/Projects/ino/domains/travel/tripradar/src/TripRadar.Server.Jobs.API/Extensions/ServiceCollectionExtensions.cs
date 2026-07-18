using Hangfire;
using Hangfire.PostgreSql;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;
using TripRadar.Server.Infrastructure.Settings;
using TripRadar.Server.Jobs.API.Jobs;

namespace TripRadar.Server.Jobs.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static void ConfigureJobsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();

        services
            .ConfigureApplicationLayer()
            .ConfigureServices()
            .ConfigureHangfireInfrastructure(configuration)
            .ConfigureJobSettings(configuration)
            .ConfigureSerpApi(configuration);
    }

    private static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        return services
            .AddScoped<DeferredDowngradeJob>()
            .AddScoped<TokenDeductionJob>()
            .AddScoped<IDeferredDowngradeJob, DeferredDowngradeJob>()
            .AddScoped<ITokenDeductionJob, TokenDeductionJob>();
    }

    private static IServiceCollection ConfigureHangfireInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("db")
            ?? throw new InvalidOperationException("Connection string 'db' is required for Hangfire.");

        services.AddHangfire(globalConfiguration => globalConfiguration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    PrepareSchemaIfNecessary = true,
                    SchemaName = "Hangfire"
                }));

        services.AddHangfireServer();
        return services;
    }

    private static IServiceCollection ConfigureJobSettings(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .Configure<JobSettings>(configuration.GetSection("JobSettings"));
    }

    private static IServiceCollection ConfigureSerpApi(this IServiceCollection services, IConfiguration configuration)
    {
        return services
            .Configure<SerpApiSettings>(configuration.GetSection("SerpApiSettings"));
    }
}
