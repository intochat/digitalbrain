using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.button")]
public partial interface IButton :
    INeuron,
    IHandle<ButtonClicked>
{
    const string DefaultInstanceName = "default";
}
