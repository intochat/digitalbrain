using System.Text.Json.Serialization;
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
    string? Status = null);

