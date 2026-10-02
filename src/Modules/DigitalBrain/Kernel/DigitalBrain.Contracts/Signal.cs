using System.Text.Json.Serialization;

namespace DigitalBrain.Contracts;

[GenerateSerializer, Alias("brain.signal")]
// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md: the
// pure model's Signal (src/DigitalBrain) carries Publisher as a NeuronId value; this one keeps
// the string the whole repo compares against. Unifying the two is the keystone of the module
// migration phase — thirteen call sites compare Publisher to raw strings today.
public record Signal
{
    // Stamped by the publishing neuron so observers can tell same-typed signals from different
    // sources apart; empty for signals born outside a neuron. Kept off the JSON wire for now.
    [Id(0), JsonIgnore] public string Publisher { get; init; } = "";
}
