namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.result")]
public abstract record Result;

// Conversation adapter result — lives in contracts so Execution persistence round-trips the type.
[GenerateSerializer]
[Alias("chat.turn-result")]
public sealed record ChatTurnResult(
    [property: Id(0)] string Answer,
    [property: Id(1)] string Author) : Result;
