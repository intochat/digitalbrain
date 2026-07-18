using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Db.Constants;
using TripRadar.ServiceDefaults;

namespace TripRadar.Server.Db;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var host = CreateHost(args);
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SetupDbContext>();
            var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            EncryptionExtensions.Configure(configuration["Encryption:UserDataKey"] ?? throw new InvalidOperationException("Encryption:UserDataKey is required."));

            try
            {
                var pending = await context.Database.GetPendingMigrationsAsync();
                if (pending.Any())
                {
                    await context.Database.MigrateAsync();
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState is "42P07" or "42701")
            {
                if (!hostEnvironment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "Database migration failed due to an existing relation. " +
                        "Resolve schema drift manually.",
                        ex);
                }

                if (!IsSchemaResetEnabled(configuration))
                {
                    throw new InvalidOperationException(
                        $"Database migration failed due to schema drift ({ex.SqlState}). " +
                        "Automatic schema recreation is disabled. " +
                        "Set TRIPRADAR_DB_ALLOW_SCHEMA_RESET_ON_RELATION_EXISTS=true " +
                        "only when you explicitly want to reset development data.",
                        ex);
                }

                Console.WriteLine(
                    $"Migration warning: {ex.MessageText}. " +
                    $"Recreating schema '{DbConstants.SchemaName}' and retrying migrations...");

                await context.Database.ExecuteSqlRawAsync(
                    $"DROP SCHEMA IF EXISTS \"{DbConstants.SchemaName}\" CASCADE;");

                await context.Database.MigrateAsync();
            }

            await context.SeedAsync();

            var connectionString = context.Database.GetConnectionString();
            if (connectionString != null)
            {
                ConfigureHangfire(connectionString);
            }

            Console.WriteLine("Database setup completed successfully.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Stripe"))
        {
            await Console.Error.WriteLineAsync($"Configuration Error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal Error during database setup: {ex.GetType().Name}");
            await Console.Error.WriteLineAsync($"Message: {ex.Message}");
            await Console.Error.WriteLineAsync($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                await Console.Error.WriteLineAsync($"Inner Exception: {ex.InnerException.GetType().Name}");
                await Console.Error.WriteLineAsync($"Inner Message: {ex.InnerException.Message}");
                await Console.Error.WriteLineAsync($"Inner StackTrace: {ex.InnerException.StackTrace}");
            }

            Environment.Exit(1);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static bool IsSchemaResetEnabled(IConfiguration configuration)
    {
        var directValue = configuration["TRIPRADAR_DB_ALLOW_SCHEMA_RESET_ON_RELATION_EXISTS"];
        if (bool.TryParse(directValue, out var isDirectEnabled))
        {
            return isDirectEnabled;
        }

        var legacyEnvValue = Environment.GetEnvironmentVariable("TRIPRADAR_DB_ALLOW_SCHEMA_RESET_ON_RELATION_EXISTS");
        return bool.TryParse(legacyEnvValue, out var isLegacyEnabled) && isLegacyEnabled;
    }

    private static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter("Microsoft.EntityFrameworkCore"))
            .WithTracing(tracing => tracing.AddEntityFrameworkCoreInstrumentation());
        builder.Services.AddDbContext<SetupDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("db"))
        );
        return builder.Build();
    }

    private static void ConfigureHangfire(string connectionString) =>
        GlobalConfiguration.Configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = true, SchemaName = "Hangfire" });
}
