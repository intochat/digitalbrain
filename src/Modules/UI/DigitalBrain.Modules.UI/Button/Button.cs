using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("button")]
internal sealed class Button : Neuron, IButton
{
    public Task HandleAsync(ButtonClicked synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        return EmitAsync(new ButtonActivated(synapse.OfferCommandId, Id, synapse.Action));
    }
}
