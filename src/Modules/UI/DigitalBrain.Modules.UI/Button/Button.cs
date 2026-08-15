using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("button")]
internal sealed class Button : Neuron, IButton
{
    public async Task HandleAsync(ButtonClicked synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // An activation with no live route journals and vanishes (zero-receiver
        // emission); the click must refuse visibly once its offer expired.
        using var lookup = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        var routes = await GrainFactory
            .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(Id.Owner).ToGrainId())
            .ConnectionsFrom(Id, ButtonActivated.AliasName)
            .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (routes.Count == 0)
        {
            throw new NeuronAuthorizationException(
                $"Button '{Id}' has no live activation route and refuses the click; its offer has expired.");
        }

        await EmitAsync(new ButtonActivated(synapse.OfferCommandId, Id, synapse.Action))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
