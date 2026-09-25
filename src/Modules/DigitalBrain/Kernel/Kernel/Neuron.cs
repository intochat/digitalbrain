using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Utilities;

namespace DigitalBrain.Core;

public abstract class Neuron : Grain, INeuron
{
    private readonly Guid _activation = Guid.NewGuid();
    private ObserverManager<INeuronObserver>? _observers;
    private ObserverManager<INeuronObserver> Observers => _observers ??= new(
        ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.ObserverLease,
        ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Neuron.Observers"));

    public virtual Task<Guid> Watch(INeuronObserver observer)
    {
        Observers.Subscribe(observer, observer);
        return Task.FromResult(_activation);
    }

    public Task Unwatch(INeuronObserver observer)
    {
        Observers.Unsubscribe(observer);
        return Task.CompletedTask;
    }

    protected Task PublishAsync(Signal signal)
    {
        if ((IntentContext.Current?.ScopeId ?? RequestContext.Get(NeuronCallObservation.ScopeKey) as string) is { } scope)
        {
            ServiceProvider.GetService<ActivityFeed>()?.Append(new ActivityEvent(scope, 0, Guid.NewGuid(),
                Guid.NewGuid(), IntentContext.Current?.IntentId, DateTimeOffset.UtcNow,
                ActivityKind.SignalPublished, this.GetGrainId().ToString(), null,
                signal.GetType().Name, "published", null, null));
        }
        ServiceProvider.GetService<LocalSignalHub>()?.Publish(this.GetGrainId(), signal);
        return Observers.Notify(observer => observer.OnSignalAsync(signal));
    }
}

public abstract class Neuron<TState>(IPersistentState<TState> store) : Neuron where TState : class, new()
{
    protected TState Snapshot
    {
        get
        {
            store.State ??= new TState();
            return store.State;
        }
    }

    protected async Task Save(TState next, Signal changed, Signal? acted = null)
    {
        store.State = next;
        await store.WriteStateAsync();
        await PublishAsync(changed);
        if (acted is not null)
        {
            await PublishAsync(acted);
        }
    }
}
