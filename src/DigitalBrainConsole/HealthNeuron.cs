using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.BroadcastChannel;

namespace DigitalBrainConsole;

[GrainType("health")]
[ImplicitChannelSubscription(DigitalBrainNames.ActivationChannelNamespace)]
internal sealed class HealthNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    IHealth,
    IHandle<DigitalBrainActivated>,
    IOnBroadcastChannelSubscribed
{
    public Task OnSubscribed(IBroadcastChannelSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return subscription.Attach<SignalDelivery>(async activation =>
        {
            _ = await Deliver(activation)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        });
    }

    public Task HandleAsync(DigitalBrainActivated signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<bool> Verify(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
