namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turns")]
public sealed record ChatTurns([property: Id(0)] IReadOnlyList<ChatTurnSnapshot> Turns);
