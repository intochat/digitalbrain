using DigitalBrain.Chat;

namespace IntoChat;

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId,
    string Signal,
    string NeuronId,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? Status = null,
    IReadOnlyList<UiCardOffer>? Cards = null,
    string? EventId = null);
