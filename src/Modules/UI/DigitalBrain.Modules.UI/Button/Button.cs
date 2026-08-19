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

        // An activation with no live route journals and vanishes (zero-receiver emission);
        // the click must refuse visibly. The guard reads the same brain connection table the
        // emission below routes through, so guard and delivery can never disagree.
        using var lookup = new CancellationTokenSource(NeuronCallTimeouts.LookupBound);
        var routes = await OwnersBrain()
            .Connections(Id, ButtonActivated.AliasName)
            .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (routes.Count == 0)
        {
            throw new NeuronAuthorizationException(
                $"Button '{Id}' has no activation route in the brain and refuses the click.");
        }

        await EmitAsync(new ButtonActivated(synapse.OfferCommandId, Id, synapse.Action))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
