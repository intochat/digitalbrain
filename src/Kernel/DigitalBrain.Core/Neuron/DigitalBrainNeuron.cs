using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.BroadcastChannel;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(IDigitalBrainNeuron.GrainTypeName)]
internal sealed class DigitalBrainNeuron : Neuron, IDigitalBrainNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

    protected override bool RegistersWithBrain => false;

    public DigitalBrainNeuron()
    {
        _activationPublished = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>(ActivationPublishedName);
    }

    public async Task Activate()
    {
        if (_activationPublished.Value)
        {
            return;
        }

        // Journal first: DigitalBrainActivated in this neuron's OWN Outgoing journal is the
        // pinned activation footprint, whether or not any surface module subscribes.
        var activated = await StageOutgoingAsync(new DigitalBrainActivated(Id.Owner), cause: null)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var writer = ServiceProvider
            .GetRequiredService<IClusterClient>()
            .GetBroadcastChannelProvider(DigitalBrainNames.BroadcastChannelProvider)
            .GetChannelWriter<SynapseDelivery>(ChannelId.Create(
                DigitalBrainNames.ActivationChannelNamespace,
                $"{Id.Owner.Value}/{DigitalBrainNames.ActivationSubscriberName}"));
        await writer.Publish(activated)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _activationPublished.Value = true;
        await WriteStateAsync().ConfigureAwait(true);
    }
}
