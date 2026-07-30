using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Flutter;

[ClientEntryPoint]
[Alias("flutter.shell")]
[Description("Flutter shell neuron")]
public partial interface IShell : INeuron
{
    [Alias(nameof(Open))]
    Task Open(OpenScene command);
}
