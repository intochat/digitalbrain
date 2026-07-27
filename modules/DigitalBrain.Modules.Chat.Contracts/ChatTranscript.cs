using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.transcript")]
public sealed record ChatTranscript(
    [property: Id(0)] IReadOnlyList<ChatTurn> Turns);
