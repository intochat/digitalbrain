using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-snapshot")]
public sealed record ChatTurnSnapshot(
    [property: Id(0)] TurnId TurnId,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatTurnStatus Status);
