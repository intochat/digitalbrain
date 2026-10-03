using DigitalBrain.Contracts.Signals;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Utilities;
using System.Reflection;

namespace DigitalBrain.Kernel;

public abstract class Neuron : Grain, INeuron, IGrainBase
{
    private readonly Guid _activation = Guid.NewGuid();
    private bool _observedActivation;
    private ObserverManager<INeuronObserver>? _observers;
    private ObserverManager<INeuronObserver> Observers => _observers ??= new(
        ServiceProvider.GetRequiredService<IOptions<ObserverOptions>>().Value.Lease,
        ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Neuron.Observers"));

    protected TimeSpan ObserverRenewal => TimeSpan.FromTicks(ServiceProvider.GetRequiredService<IOptions<ObserverOptions>>().Value.Lease.Ticks / 5);

    // Orleans calls IGrainBase; keep derived virtual hooks intact without relying on a base call.
    async Task IGrainBase.OnActivateAsync(CancellationToken cancellationToken)
    {
        await OnActivateAsync(cancellationToken);
        _observedActivation = true;
        PublishActivity(active: true);
    }

    async Task IGrainBase.OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        try { await OnDeactivateAsync(reason, cancellationToken); }
        finally { if (_observedActivation) { PublishActivity(active: false); } }
    }

    private void PublishActivity(bool active)
    {
        var signals = ServiceProvider.GetService<LocalSignalHub>();
        if (signals is null) { return; }
        var typeIds = GetType().GetInterfaces()
            .Where(type => type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<AliasAttribute>()?.Alias)
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var now = ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        NeuronActivity activity = active
            ? new NeuronActivated { NeuronId = this.GetGrainId().ToString(), Key = this.GetPrimaryKeyString(), TypeIds = typeIds, ActivationId = _activation, ObservedAt = now }
            : new NeuronDeactivated { NeuronId = this.GetGrainId().ToString(), Key = this.GetPrimaryKeyString(), TypeIds = typeIds, ActivationId = _activation, ObservedAt = now };
        signals.Publish(activity);
    }

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
        var stamped = signal with { Publisher = this.GetGrainId().ToString() };
        ServiceProvider.GetService<LocalSignalHub>()?.Publish(this.GetGrainId(), stamped);
        return Observers.Notify(observer => observer.OnSignalAsync(stamped));
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
