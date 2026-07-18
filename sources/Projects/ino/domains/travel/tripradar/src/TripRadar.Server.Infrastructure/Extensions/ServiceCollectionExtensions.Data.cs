using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TripRadar.Server.Db;
using TripRadar.Server.Infrastructure.Database;
using TripRadar.Server.Infrastructure.Database.Interceptors;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection ConfigureDatabase(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var isDevEnv = environment.IsDevelopment();

        services.AddSingleton<BlindIndexSaveChangesInterceptor>();
        var dataSource = GetDataSources(configuration);

        return services.AddEntityFrameworkNpgsql()
            .AddDbContext<SetupDbContext>()
            .AddDbContextPool<TripRadarDbContext>((serviceProvider, options) =>
            {
                options.UseNpgsql(dataSource, o =>
                    {
                        o.CommandTimeout(230);
                        o.MigrationsHistoryTable("__EFMigrationsHistory", "TripRadar");
                    }).EnableDetailedErrors(isDevEnv).EnableSensitiveDataLogging(isDevEnv);

                if (isDevEnv)
                {
                    options.LogTo(Console.WriteLine);
                }
                options.UseInternalServiceProvider(serviceProvider);

                var interceptor = serviceProvider.GetService<BlindIndexSaveChangesInterceptor>();
                if (interceptor != null)
                {
                    options.AddInterceptors(interceptor);
                }
            });
    }

    private static NpgsqlDataSource GetDataSources(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("db");
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        return builder.Build();
    }
}
