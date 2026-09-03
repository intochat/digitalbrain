using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.BroadcastChannel;

namespace DigitalBrainConsole;

[GrainType("console")]
[ImplicitChannelSubscription(DigitalBrainNames.ActivationChannelNamespace)]
internal sealed class ConsoleNeuron(NeuronRuntime runtime) :
    Neuron(runtime),
    IConsole,
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
        if (signal.Owner != Id.Owner)
        {
            return Task.CompletedTask;
        }

        return Attach(cancellationToken);
    }

    public Task Attach(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("digitalbrain console attached");
        return Task.CompletedTask;
    }
}
