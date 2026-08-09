using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.button")]
[Description("Interactive button control")]
public partial interface IButton : INeuron
{
    const string DefaultInstanceName = "default";
}
