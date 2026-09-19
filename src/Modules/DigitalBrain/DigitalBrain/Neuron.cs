using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.BroadcastChannel;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal static class NeuronChannels
{
    public static ChannelId Everyone { get; } = ChannelId.Create(NeuronBroadcast.Provider, NeuronBroadcast.Everyone);

    public static ChannelId For(string neuronId) => ChannelId.Create(NeuronBroadcast.Provider, neuronId);
}


[GrainType("neuron")]
public class Neuron : Grain, INeuron
{
    public Task Ping() => Task.CompletedTask;

    public Task Sleep()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await Broadcast(new NeuronActivated(this.GetPrimaryKeyString()));
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await Broadcast(new NeuronDeactivated(this.GetPrimaryKeyString(), reason.ReasonCode.ToString()));
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    private Task Broadcast(Signal signal)
    {
        var channels = ServiceProvider.GetRequiredService<IClusterClient>()
            .GetBroadcastChannelProvider(NeuronBroadcast.Provider);
        return Task.WhenAll(
            channels.GetChannelWriter<Signal>(NeuronChannels.Everyone).Publish(signal),
            channels.GetChannelWriter<Signal>(NeuronChannels.For(this.GetPrimaryKeyString())).Publish(signal));
    }
}
