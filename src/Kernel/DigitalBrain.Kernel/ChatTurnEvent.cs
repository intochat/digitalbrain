using System.Text.Json.Serialization;
using DigitalBrain.Chat;
namespace DigitalBrain.Kernel;

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId,
    string Synapse,
    string NeuronId,
    string Caller,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string? TurnId = null,
    string? Status = null,
    KitCardOffer[]? Cards = null);

