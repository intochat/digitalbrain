using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class OccupiedNeuronProbe :
    Neuron,
    IOccupiedNeuronProbe,
    IEmit<Greeted>
{
    internal const string HoldingMessage = "holding the turn open";

    public Task Announce(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return EmitAsync(new Greeted(message));
    }

    public async Task HoldTurn(TimeSpan duration)
    {
        await EmitAsync(new Greeted(HoldingMessage));
        await Task.Delay(duration);
    }
}
