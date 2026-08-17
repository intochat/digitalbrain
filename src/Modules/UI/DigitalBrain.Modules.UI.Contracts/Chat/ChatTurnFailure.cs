using DigitalBrain.Execution;

namespace DigitalBrain.Chat;

[GenerateSerializer, Alias("chat.turn-failure")]
public sealed record ChatTurnFailure(
    [property: Id(0)] string Reason) : Failure;
