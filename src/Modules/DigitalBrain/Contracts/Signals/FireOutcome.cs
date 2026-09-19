using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

// What emit minted, and how many scenario listeners accepted the fact.
[GenerateSerializer]
[Alias("db.fire-outcome")]
public sealed record FireOutcome(
    [property: Id(0)] SignalId SignalId,
    [property: Id(1)] CorrelationId CorrelationId,
    [property: Id(2)] int Delivered,
    [property: Id(3)] int Busy)
{
    public FireOutcome RequireAccepted()
    {
        if (Busy > 0)
        {
            throw new NeuronBusyException($"Signal '{SignalId}' target was busy; the reaction will be retried.");
        }

        return this;
    }
}
