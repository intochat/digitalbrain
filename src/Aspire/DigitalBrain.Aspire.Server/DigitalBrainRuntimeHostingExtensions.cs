using DigitalBrain.Client.Orleans;
using Azure.Data.Tables;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Storage;

namespace DigitalBrain.Aspire.Server;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainServer(this IHostApplicationBuilder builder, Action<DigitalBrainServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration[DigitalBrainNames.ConfigurationFileKey] is { Length: > 0 } configurationFile)
        { builder.Configuration.AddJsonFile(configurationFile, optional: false, reloadOnChange: false); }

        ArgumentNullException.ThrowIfNull(configure);
        var definition = new DigitalBrainServerBuilder();
        configure(definition);
        builder.Services.AddSingleton(definition);
        builder.Services.AddHostedService<AccessPolicyValidation>();
        if (definition.AzureStorage)
        {
            builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
            builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Reminders);
            builder.AddKeyedAzureBlobServiceClient(DigitalBrainNames.GrainState);
        }
        var modules = definition.SelectModules(builder.Configuration);
        builder.UseOrleans(silo =>
        {
            if (definition.AzureStorage) { ConfigureStandaloneAzureClustering(silo, builder.Configuration); }
            silo.Services.AddDigitalBrainClient();
            if (definition.AzureStorage)
            {
                silo.Services.AddKeyedSingleton<IGrainStorageSerializer>(
                    DigitalBrainNames.DefaultGrainStorage,
                    static (services, _) => new OrleansGrainStorageSerializer(
                        services.GetRequiredService<Orleans.Serialization.Serializer>()));
                silo.Services.AddOptions<AzureBlobStorageOptions>(DigitalBrainNames.DefaultGrainStorage)
                    .Configure(static options => options.ContainerName = DigitalBrainNames.GrainStateContainer);
            }
            // Orleans hosting already registers one activity-propagation filter pair. An explicit
            // `silo.AddActivityPropagation()` here registered a second pair and doubled every grain
            // call to 4 spans (measured); it was removed so exactly one registration remains.
            silo.AddDigitalBrain(modules.Select(module => module.GetType()));
            foreach (var module in modules)
            {
                module.Configure(silo);
                silo.Services.AddSingleton(module);
            }
            if (definition.Dashboard)
            {
                silo.AddDashboard(options =>
            {
                options.CounterUpdateIntervalMs = 5000;
                options.HistoryLength = 200;
            });
            }
        });
        return builder;
    }

    public static WebApplication MapDigitalBrainDashboard(this WebApplication app)
    {
        if (app.Services.GetRequiredService<DigitalBrainServerBuilder>().Dashboard)
        { app.MapOrleansDashboard(DigitalBrainNames.OrleansDashboardPath); }
        return app;
    }

    private sealed class AccessPolicyValidation(IServiceProvider services) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = services.GetRequiredService<DigitalBrain.Kernel.Enforcement.IBrainAccess>();
            if (!services.GetServices<DigitalBrain.Kernel.Enforcement.ICallFilterStage>().Any())
            { throw new InvalidOperationException("Register an authorization policy or AddDigitalBrainPlatform before starting the server."); }
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static void ConfigureStandaloneAzureClustering(ISiloBuilder silo, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Orleans:Clustering:ProviderType"]))
        {
            return;
        }

        var clustering = configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (string.IsNullOrWhiteSpace(clustering))
        {
            if (configuration.GetSection(StandaloneOptions.SectionName).Get<StandaloneOptions>()?.Standalone == true)
            {
                throw new InvalidOperationException(
                    $"Missing connection string '{DigitalBrainNames.Clustering}'. "
                    + "Standalone hosts require Azure Table clustering (or Orleans:Clustering:ProviderType).");
            }

            return;
        }

        silo.UseAzureStorageClustering(options =>
            options.TableServiceClient = new TableServiceClient(clustering));

        var reminders = configuration.GetConnectionString(DigitalBrainNames.Reminders)
            ?? clustering;
        silo.UseAzureTableReminderService(reminders);
    }
}
