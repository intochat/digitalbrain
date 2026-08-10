using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.surface")]
public partial interface ISurface :
    INeuron,
    IHandle<OpenSurface>,
    IHandle<ControlActivated>
{
    const string DefaultInstanceName = "desk";
}
