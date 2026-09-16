using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

[GenerateSerializer, Alias("chat.turn-accepted")]
public sealed record TurnAccepted(
    [property: Id(0)] SignalId Turn,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] DateTimeOffset AcceptedAt);
