using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

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
