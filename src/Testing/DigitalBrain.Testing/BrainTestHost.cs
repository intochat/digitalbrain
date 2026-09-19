using DigitalBrain.Core;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Orleans.Storage;
using Orleans.Runtime;
namespace DigitalBrain.Testing;
public sealed class BrainTestHost : IAsyncDisposable
{
    private readonly InProcessTestCluster _cluster;
    private readonly BrainClient _brain;
    private readonly List<BehaviorRun> _behaviors = [];
    private readonly List<IAsyncDisposable> _probes = [];
    private readonly FileStream? _storeLease;
    private readonly string? _temporaryStore;
    private readonly StorageFaults? _faults;
    private int _disposed;
    private BrainTestHost(InProcessTestCluster cluster, FileStream? storeLease, string? temporaryStore, StorageFaults? faults)
    {
        _cluster = cluster;
        _brain = cluster.Client.ServiceProvider.GetRequiredService<BrainClient>();
        _storeLease = storeLease; _temporaryStore = temporaryStore; _faults = faults;
    }
    public IDigitalBrain Brain => _brain;
    public IGrainFactory Grains => _cluster.Client;
    public BehaviorRun RunBehavior(Func<IDigitalBrain, CancellationToken, Task> body, CancellationToken ct = default)
    {
        var run = new BehaviorRun(Brain, body, ct);
        _behaviors.Add(run);
        return run;
    }
    public async Task<SignalProbe<T>> ObserveAsync<T>(INeuron source, CancellationToken ct = default) where T : Signal
    {
        var probe = new SignalProbe<T>(await Brain.SubscribeAsync<T>(source, ct).ConfigureAwait(false));
        _probes.Add(probe);
        return probe;
    }
    public static async Task<BrainTestHost> StartAsync(BrainTestOptions? options = null, CancellationToken cancellationToken = default)
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
        builder.ConfigureHost(host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain();
            if (directory is null) { silo.AddMemoryGrainStorage("Default"); }
            else
            {
                silo.Services.AddKeyedSingleton<IGrainStorage>("Default", (services, _) =>
                {
                    IGrainStorage store = new FileGrainStorage(directory, services.GetRequiredService<Orleans.Serialization.Serializer>(), storageGate);
                    return options.StorageFaults is { } faults ? new FaultingGrainStorage(store, faults) : store;
                });
            }
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        InProcessTestCluster? cluster = null;
        try
        {
            cluster = builder.Build();
            await cluster.DeployAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(cluster, lease, temporary, options.StorageFaults);
        }
        catch
        {
            options.StorageFaults?.Dispose();
            try { if (cluster is not null) { await cluster.DisposeAsync().ConfigureAwait(false); } }
            finally { lease?.Dispose(); if (temporary is not null) { Directory.Delete(temporary, true); } }
            throw;
        }
    }
    public async Task RestartSiloAsync(CancellationToken cancellationToken = default)
    {
        await _cluster.RestartSiloAsync(_cluster.GetActiveSilos().Single()).WaitAsync(cancellationToken).ConfigureAwait(false);
        await _cluster.WaitForLivenessToStabilizeAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        List<Exception> failures = [];
        _faults?.Dispose();
        foreach (var resource in _behaviors.Cast<IAsyncDisposable>().Concat(_probes).Append(_brain))
        {
            try { await resource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _cluster.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        _storeLease?.Dispose();
        if (_temporaryStore is not null) { Directory.Delete(_temporaryStore, true); }
        if (failures.Count > 0) { throw new AggregateException("Brain test cleanup failed.", failures); }
    }
}
