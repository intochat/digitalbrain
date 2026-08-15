using DigitalBrain.Execution;

namespace DigitalBrain.Chat;

// Conversation adapter result — contracts so Execution persistence round-trips the type.
[GenerateSerializer]
[Alias("chat.turn-result")]
public sealed record ChatTurnResult(
    [property: Id(0)] string Answer,
    [property: Id(1)] string Author) : Result;