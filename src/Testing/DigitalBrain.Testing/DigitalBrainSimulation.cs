using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class DigitalBrainSimulation
{
    public static async Task<IDigitalBrain> StartAsync(SimulationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var temporary = options.PersistenceDirectory is null && options.StorageFaults is not null
            ? Path.Combine(Path.GetTempPath(), "brain-" + Guid.NewGuid().ToString("N")) : null;
        var directory = options.PersistenceDirectory ?? temporary;
        FileStream? lease = null;
        var storageGate = new SemaphoreSlim(1);
        if (directory is not null)
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            lease = new FileStream(Path.Combine(directory, ".owner"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
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
            if (directory is null) { silo.AddMemoryGrainStorage("Default"); }
            else
            {
                silo.Services.AddKeyedSingleton<IGrainStorage>("Default", (services, _) =>
                {
                    IGrainStorage store = new FileGrainStorage(directory, services.GetRequiredService<Orleans.Serialization.Serializer>(), storageGate);
                    return options.StorageFaults is { } faults ? new FaultingGrainStorage(store, faults) : store;
                });
            }
            if (options.UseReminders)
            {
                if (directory is null) { silo.UseInMemoryReminderService(); }
                else
                {
                    silo.AddReminders();
                    silo.Services.AddSingleton<IReminderTable>(new FileReminderTable(directory));
                }
            }
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
            return new SimulatedBrain(cluster, web, http, lease, temporary, options.StorageFaults);
        }
        catch
        {
            await SimulatedBrain.ReleaseAsync(cluster, web, http, lease, temporary, options.StorageFaults).ConfigureAwait(false);
            throw;
        }
    }
}
