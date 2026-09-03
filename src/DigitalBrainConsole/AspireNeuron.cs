using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.BroadcastChannel;

namespace DigitalBrainConsole;

[GrainType("aspire")]
[ImplicitChannelSubscription(DigitalBrainNames.ActivationChannelNamespace)]
internal sealed class AspireNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    IAspire,
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

    public Task StartDistributedApp(string? appHostProject = null, CancellationToken cancellationToken = default)
        => AspireApp.StartDistributedAppAsync(appHostProject, cancellationToken);
}
