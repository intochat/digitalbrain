using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

internal sealed class ButtonActivatedToShowTime : ISynapseTransform
{
    internal const string TransformName = "ui.button-activated->chat.show-time";

    public string Name => TransformName;

    public Synapse Apply(Synapse synapse)
        => synapse is ButtonActivated activated
            ? new ShowTime(activated.OfferCommandId)
            : throw new InvalidOperationException(
                $"Transform '{Name}' cannot adapt a '{synapse.GetType().Name}'.");
}
