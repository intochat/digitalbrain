using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Execution;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("chat.turn-goal")]
public sealed record ChatTurnGoal(
    [property: Id(0)] Guid TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ActorContext Actor,
    [property: Id(4)] NeuronId Chat) : Goal;
