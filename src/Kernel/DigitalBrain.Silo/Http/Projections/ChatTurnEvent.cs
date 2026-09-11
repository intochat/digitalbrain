using DigitalBrain.Chat;

namespace DigitalBrain.Kernel;

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
    IReadOnlyList<KitCardOffer>? Cards = null,
    string? EventId = null);
