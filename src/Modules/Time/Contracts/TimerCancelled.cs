using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-cancelled")]
public sealed record TimerCancelled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Timer,
    [property: Id(2)] long Generation) : Synapse;

