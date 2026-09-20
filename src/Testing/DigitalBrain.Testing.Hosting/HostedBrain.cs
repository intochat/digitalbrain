using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;
using Aspire.Hosting;

namespace DigitalBrain.Testing.Hosting;

public abstract class HostedBrain : IDigitalBrain, ITrackedBrain
{
    protected HostedBrain(AspireTestSession session)
    {
        Session = session;
        Lifetime = session.Lifetime;
    }
    protected AspireTestSession Session { get; }
    protected TestSessionLifetime Lifetime { get; }
    public HttpClient HttpClient => Session.HttpClient;
    public DistributedApplication Application => Session.App;
    int ITrackedBrain.BufferCapacity => new BrainOptions().BufferCapacity;
    TestExecutionOptions ITrackedBrain.Execution => Session.Options;
    void ITrackedBrain.Track(IAsyncDisposable resource) => Lifetime.Own("observation", resource);
    public T Get<T>(string id) where T : class, IGrainWithStringKey => Session.Brain.Get<T>(id);
    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => Session.Brain.SubscribeAsync<T>(source, cancellationToken);
    public virtual ValueTask DisposeAsync() => Lifetime.DisposeAsync();
}
