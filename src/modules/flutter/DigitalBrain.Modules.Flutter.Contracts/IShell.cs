using DigitalBrain.Abstractions;

namespace DigitalBrain.Flutter;

[ClientEntryPoint]
public partial interface IShell : INeuron
{
    [Alias(nameof(Open))]
    Task Open(OpenScene command);
}
