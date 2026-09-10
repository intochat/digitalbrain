using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.turn-snapshot")]
public sealed record ChatTurnSnapshot(
    [property: Id(0)] SignalId Turn,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatTurnStatus Status,
    [property: Id(4)] DateTimeOffset StartedAt,
    [property: Id(5)] DateTimeOffset? SettledAt = null,
    [property: Id(6)] string? Answer = null,
    [property: Id(7)] string? Author = null,
    [property: Id(8)] string? Detail = null,
    [property: Id(9)] IReadOnlyList<KitCardOffer>? Cards = null);
