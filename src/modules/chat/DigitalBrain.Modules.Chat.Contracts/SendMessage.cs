using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.send-message")]
public sealed record SendMessage([property: Id(0)] CommandId CommandId, [property: Id(1)] string Text);
