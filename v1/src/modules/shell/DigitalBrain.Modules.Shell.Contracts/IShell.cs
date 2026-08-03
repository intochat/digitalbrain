using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[ClientEntryPoint]
[Alias("flutter.shell")]
[Description("Shell neuron")]
public partial interface IShell : INeuron
{
    [Alias(nameof(Open))]
    Task Open(OpenScene command);
}
