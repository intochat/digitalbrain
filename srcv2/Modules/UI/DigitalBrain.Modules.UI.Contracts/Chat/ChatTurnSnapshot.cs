using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-snapshot")]
public sealed record ChatTurnSnapshot(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatTurnStatus Status,
    [property: Id(4)] string? ExecutionName = null);
