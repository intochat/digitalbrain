using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[Alias("ui.surface")]
[Description("Owner UI surface (window/desk); many instances allowed")]
public partial interface ISurface : INeuron
{
    const string DefaultInstanceName = "desk";
}
