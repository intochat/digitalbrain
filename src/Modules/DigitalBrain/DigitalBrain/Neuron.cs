using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.BroadcastChannel;
using Orleans.Runtime;
using Orleans.Utilities;

namespace DigitalBrain.Core;

internal static class NeuronChannels
{
    public static ChannelId Everyone { get; } = ChannelId.Create(NeuronBroadcast.Provider, NeuronBroadcast.Everyone);

    public static ChannelId For(string neuronId) => ChannelId.Create(NeuronBroadcast.Provider, neuronId);
}

[GrainType("neuron")]
public class Neuron : Grain, INeuron
{
    private readonly List<INeuron> _listeners = [];
    private readonly List<Signal> _inbox = [];
    private ObserverManager<INeuronObserver>? _watchers;

    public Task Ping() => Task.CompletedTask;

    public Task Sleep()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task Bind(INeuron listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (_listeners.All(bound => bound.GetGrainId() != listener.GetGrainId()))
        {
            _listeners.Add(listener);
        }

        return Task.CompletedTask;
    }

    public Task Watch(INeuronObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Watchers.Subscribe(observer, observer);
        return Task.CompletedTask;
    }

    public Task Unwatch(INeuronObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Watchers.Unsubscribe(observer);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Signal>> Inbox() => Task.FromResult<IReadOnlyList<Signal>>(_inbox.ToArray());

    public async Task Receive(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _inbox.Add(signal);
        await OnReceived(signal);
    }

    public async Task Broadcast(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        await Publish(signal);
        Watchers.Notify(watcher => watcher.Hear(signal).Ignore());
        await Task.WhenAll(_listeners.Select(listener => listener.Receive(signal)));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Publish(new NeuronActivated(this.GetPrimaryKeyString()));
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await Publish(new NeuronDeactivated(this.GetPrimaryKeyString(), reason.ReasonCode.ToString()));
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    protected virtual Task OnReceived(Signal signal) => Task.CompletedTask;

    private ObserverManager<INeuronObserver> Watchers =>
        _watchers ??= new ObserverManager<INeuronObserver>(
            TimeSpan.FromMinutes(5),
            ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Neuron.Watchers"));

    private Task Publish(Signal signal)
    {
        var channels = ServiceProvider.GetRequiredService<IClusterClient>()
            .GetBroadcastChannelProvider(NeuronBroadcast.Provider);
        return Task.WhenAll(
            channels.GetChannelWriter<Signal>(NeuronChannels.Everyone).Publish(signal),
            channels.GetChannelWriter<Signal>(NeuronChannels.For(this.GetPrimaryKeyString())).Publish(signal));
    }
}
