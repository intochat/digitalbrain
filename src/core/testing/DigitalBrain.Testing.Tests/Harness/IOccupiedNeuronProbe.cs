using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

[ClientEntryPoint]
[Alias("harness.occupied-neuron-probe")]
[Description("Harness neuron that holds its turn open so a journal read can be attempted mid-turn")]
public partial interface IOccupiedNeuronProbe : INeuron
{
    [Alias(nameof(Announce))]
    Task Announce(string message);

    [Alias(nameof(HoldTurn))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task HoldTurn(TimeSpan duration);
}
