namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("chat.turn-failure")]
public sealed record ChatTurnFailure(
    [property: Id(0)] string Reason) : Failure;

