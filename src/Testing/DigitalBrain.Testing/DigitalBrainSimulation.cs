using DigitalBrain.Contracts;
using DigitalBrain.Core;
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
        try
        {
            cluster = builder.Build();
            await cluster.DeployAsync(cancellationToken).ConfigureAwait(false);
            return new SimulatedBrain(cluster, lease, temporary, options.StorageFaults);
        }
        catch
        {
            options.StorageFaults?.Dispose();
            try { if (cluster is not null) { await cluster.DisposeAsync().ConfigureAwait(false); } }
            finally { lease?.Dispose(); if (temporary is not null) { Directory.Delete(temporary, true); } }
            throw;
        }
    }
}

internal sealed class SimulatedBrain(InProcessTestCluster cluster, FileStream? storeLease, string? temporaryStore, StorageFaults? faults)
    : IDigitalBrain
{
    private readonly IDigitalBrain _brain = cluster.Client.ServiceProvider.GetRequiredService<IDigitalBrain>();
    private readonly List<IAsyncDisposable> _resources = [];
    private int _disposed;

    public IGrainFactory Grains => cluster.Client;

    internal IDigitalBrain Client => _brain;

    internal void Track(IAsyncDisposable resource) => _resources.Add(resource);

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _brain.SubscribeAsync<T>(source, cancellationToken);

    public Task DeactivateAsync(INeuron neuron, CancellationToken cancellationToken = default)
        => cluster.DeactivateAsync(neuron.GetGrainId()).WaitAsync(cancellationToken);

    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        await cluster.RestartSiloAsync(cluster.GetActiveSilos().Single()).WaitAsync(cancellationToken).ConfigureAwait(false);
        await cluster.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        List<Exception> failures = [];
        faults?.Dispose();
        foreach (var resource in _resources)
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _brain.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        try { await cluster.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        storeLease?.Dispose();
        if (temporaryStore is not null) { Directory.Delete(temporaryStore, true); }
        if (failures.Count > 0) { throw new AggregateException("Brain simulation cleanup failed.", failures); }
    }
}
