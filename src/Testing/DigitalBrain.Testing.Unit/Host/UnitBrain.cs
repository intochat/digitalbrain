using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;

namespace DigitalBrain.Testing.Unit;

public sealed class UnitBrain : IDigitalBrain, ITrackedBrain
{
    private readonly InProcessTestCluster cluster;
    private readonly IDigitalBrain _brain;
    private readonly TestSessionLifetime _lifetime;
    private readonly TestExecutionOptions _execution;
    internal UnitBrain(InProcessTestCluster cluster, TestExecutionOptions execution, TestSessionLifetime lifetime)
    {
        this.cluster = cluster;
        _execution = execution;
        _brain = cluster.Client.ServiceProvider.GetRequiredService<IDigitalBrain>();
        _lifetime = lifetime;
        _lifetime.Own("client", _brain);
    }
    int ITrackedBrain.BufferCapacity => cluster.Client.ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.BufferCapacity;
    TestExecutionOptions ITrackedBrain.Execution => _execution;

    public IGrainFactory Grains => cluster.Client;

    internal IDigitalBrain Client => _brain;

    void ITrackedBrain.Track(IAsyncDisposable resource) => _lifetime.Own("observation", resource);

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

    public ValueTask DisposeAsync() => _lifetime.DisposeAsync();

}