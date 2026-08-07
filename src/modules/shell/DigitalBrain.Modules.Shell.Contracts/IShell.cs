using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[Alias("flutter.shell")]
[Description("Shell neuron")]
public partial interface IShell : INeuron
{
    const string DefaultInstanceName = "desk";
}
