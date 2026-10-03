using System.Text.Json.Serialization;

namespace DigitalBrain;

[GenerateSerializer, Alias("brain.signal")]
public record Signal
{
    // Stamped by the publishing neuron so observers can tell same-typed signals from different
    // sources apart; empty for signals born outside a neuron. Kept off the JSON wire for now.
    [Id(0), JsonIgnore] public string Publisher { get; init; } = "";
}
