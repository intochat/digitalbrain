namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn")]
public sealed record ChatTurn([property: Id(0)] bool FromUser, [property: Id(1)] string Text);
