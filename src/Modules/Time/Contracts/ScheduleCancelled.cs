using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.schedule-cancelled")]
public sealed record ScheduleCancelled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Schedule,
    [property: Id(2)] long Generation) : Synapse;

