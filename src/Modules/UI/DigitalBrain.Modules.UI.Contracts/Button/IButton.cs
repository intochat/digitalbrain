using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.button")]
[Description("Interactive button control with identity; clicks become routable activations")]
public partial interface IButton :
    INeuron,
    IHandle<ButtonClicked>,
    IEmit<ButtonActivated>
{
    const string DefaultInstanceName = "default";
}
