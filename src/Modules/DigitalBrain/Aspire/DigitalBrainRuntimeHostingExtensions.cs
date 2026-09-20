using Azure.Data.Tables;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Dashboard;
using Orleans.Storage;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrain(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Configuration["DigitalBrain:Testing:PrivateConfiguration"] is { Length: > 0 } privatePath)
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(privatePath))
                ?? throw new InvalidOperationException("Invalid private configuration.");
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Reminders);
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainNames.GrainState);
        builder.UseOrleans(silo =>
        {
            ConfigureStandaloneAzureClustering(silo, builder.Configuration);
            silo.Services.AddKeyedSingleton<IGrainStorageSerializer>(
                DigitalBrainNames.DefaultGrainStorage,
                static (services, _) => new OrleansGrainStorageSerializer(
                    services.GetRequiredService<Orleans.Serialization.Serializer>()));
            silo.Services.AddOptions<AzureBlobStorageOptions>(DigitalBrainNames.DefaultGrainStorage)
                .Configure(static options => options.ContainerName = "digitalbrain-v2-state");
            silo.AddAzureBlobJournal(builder.Configuration);
            silo.AddActivityPropagation();
            silo.AddDigitalBrain();
            foreach (var module in LoadModules(builder.Configuration))
            {
                module.Configure(silo);
                silo.Services.AddSingleton(module);
            }
            silo.AddDashboard(options =>
            {
                options.CounterUpdateIntervalMs = 5000;
                options.HistoryLength = 200;
            });
        });
        return builder;
    }

    public static IEndpointRouteBuilder MapDigitalBrainModules(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.Configure(endpoints);
        }

        return endpoints;
    }

    private static IReadOnlyList<IModule> LoadModules(IConfiguration configuration)
    {
        var names = configuration.GetSection("DigitalBrain:Modules").Get<string[]>() ?? [];
        var modules = new List<IModule>(names.Length);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = Type.GetType(name, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not IModule module)
            {
                throw new InvalidOperationException($"{name} must implement {nameof(IModule)}.");
            }

            modules.Add(module);
        }

        return modules;
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
