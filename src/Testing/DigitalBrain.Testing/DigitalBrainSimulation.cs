using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class DigitalBrainSimulation
{
    public static async Task<IDigitalBrain> StartAsync(SimulationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        builder.ConfigureHost(host =>
        {
            host.Logging.SetMinimumLevel(LogLevel.Warning);
            if (options.Configuration is { Count: > 0 } configuration)
            { host.Configuration.AddInMemoryCollection(configuration); }
        });
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain();
            foreach (var module in options.Modules) { module.Configure(silo); }
            silo.AddMemoryGrainStorage("Default");
            if (options.StorageFaults is { } faults)
            {
                DecorateKeyed<IGrainStorage>(silo.Services, "Default", inner => new FaultingGrainStorage(inner, faults));
            }
            if (options.UseReminders) { silo.UseInMemoryReminderService(); }
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        InProcessTestCluster? cluster = null;
        WebApplication? web = null;
        HttpClient? http = null;
        try
        {
            cluster = builder.Build();
            await cluster.DeployAsync(cancellationToken).ConfigureAwait(false);
            if (options.UseHttp)
            {
                var webBuilder = WebApplication.CreateBuilder();
                webBuilder.WebHost.UseUrls("http://127.0.0.1:0");
                webBuilder.Logging.ClearProviders();
                webBuilder.Services.AddSingleton<IGrainFactory>(cluster.Client);
                webBuilder.Services.AddSingleton<IClusterClient>(cluster.Client);
                web = webBuilder.Build();
                foreach (var module in options.Modules) { module.Configure(web); }
                await web.StartAsync(cancellationToken).ConfigureAwait(false);
                http = new HttpClient { BaseAddress = new Uri(web.Urls.Single() + "/") };
            }
            return new SimulatedBrain(cluster, web, http, options.StorageFaults);
        }
        catch
        {
            await SimulatedBrain.ReleaseAsync(cluster, web, http, options.StorageFaults).ConfigureAwait(false);
            throw;
        }
    }

    private static void DecorateKeyed<T>(IServiceCollection services, object key, Func<T, T> decorate) where T : class
    {
        var descriptor = services.Last(d => d.IsKeyedService && d.ServiceType == typeof(T) && Equals(d.ServiceKey, key));
        services.Remove(descriptor);
        services.AddKeyedSingleton<T>(key, (provider, k) =>
        {
            var inner = descriptor.KeyedImplementationFactory is not null
                ? (T)descriptor.KeyedImplementationFactory(provider, k)!
                : descriptor.KeyedImplementationInstance is T instance ? instance
                : (T)ActivatorUtilities.CreateInstance(provider, descriptor.KeyedImplementationType!);
            return decorate(inner);
        });
    }
}
