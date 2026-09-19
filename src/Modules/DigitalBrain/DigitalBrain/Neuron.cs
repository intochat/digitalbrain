using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Utilities;

namespace DigitalBrain.Core;

public abstract class Neuron : Grain, INeuron
{
    private readonly Guid _activation = Guid.NewGuid();
    private ObserverManager<INeuronObserver>? _observers;
    private ObserverManager<INeuronObserver> Observers => _observers ??= new(
        ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.ObserverLease,
        ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Neuron.Observers"));

    public Task<Guid> Watch(INeuronObserver observer)
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
        return Observers.Notify(observer => observer.OnSignalAsync(signal));
    }
}
