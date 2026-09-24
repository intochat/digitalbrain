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
        var previous = store.State;
        store.State = next;
        try
        {
            await store.WriteStateAsync();
        }
        catch
        {
            store.State = previous;
            throw;
        }
        await PublishAsync(changed);
        if (acted is not null)
        {
            await PublishAsync(acted);
        }
    }
}