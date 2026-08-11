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

[GenerateSerializer]
[Alias("chat.turn-result")]
public sealed record ChatTurnResult(
    [property: Id(0)] string Answer,
    [property: Id(1)] string Author) : Result;

[GenerateSerializer]
[Alias("chat.turn-failure")]
public sealed record ChatTurnFailure(
    [property: Id(0)] string Reason) : Failure;

[GenerateSerializer]
[Alias("chat.complete-turn-work")]
public sealed record CompleteTurnWork(
    [property: Id(0)] Guid TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] ChatTurnStatus Status,
    [property: Id(3)] string? Text = null,
    [property: Id(4)] string? Author = null,
    [property: Id(5)] string? Detail = null) : Synapse;
