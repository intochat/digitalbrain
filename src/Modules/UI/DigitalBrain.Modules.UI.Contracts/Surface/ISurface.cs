using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.surface")]
[Description("Owner UI surface (window/desk); many instances allowed")]
public partial interface ISurface :
    INeuron,
    IHandle<OpenSurface>,
    IHandle<ControlActivated>,
    IEmit<SurfaceOpened>
{
    const string DefaultInstanceName = "desk";
}
