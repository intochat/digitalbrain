using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.signal-outcome")]
public sealed record SignalOutcome(
    [property: Id(0)] SignalId SignalId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] int Handled,
    [property: Id(3)] int Busy)
{
    public SignalOutcome RequireAccepted()
    {
        if (Busy > 0)
        {
            throw new NeuronBusyException($"Signal '{SignalId}' target was busy; the reaction will be retried.");
        }

        return this;
    }
}
