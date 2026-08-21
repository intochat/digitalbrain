using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Orleans.BroadcastChannel;

namespace DigitalBrain.UI;

// Receives DigitalBrainActivated over the activation BroadcastChannel: the implicit channel
// subscription activates surface-boot:{owner}/default from the channel key, and the published
// delivery runs through the regular Deliver path so it journals and dispatches like any send.
[GrainType("surface-boot")]
[ImplicitChannelSubscription(DigitalBrainNames.ActivationChannelNamespace)]
internal sealed class SurfaceBoot :
    Neuron,
    IHandle<DigitalBrainActivated>,
    IOnBroadcastChannelSubscribed
{
    public const string InstanceName = "default";
    public const string DefaultSurfaceName = ISurface.DefaultInstanceName;
    public const string HomeSurfaceKey = "home";
    public const string HomeSurfaceTitle = "Home";

    public Task OnSubscribed(IBroadcastChannelSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return subscription.Attach<SynapseDelivery>(activation => Deliver(activation));
    }

    public Task HandleAsync(DigitalBrainActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (synapse.Owner != Id.Owner)
        {
            return Task.CompletedTask;
        }

        return SendAsync(
            NeuronId.For<IUIRenderer>(Id.Owner, DefaultSurfaceName),
            new OpenSurface(CommandId.New(), HomeSurfaceKey, HomeSurfaceTitle));
    }
}
