using System.Text.Json.Serialization;
using DigitalBrain.UI;

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
    ChatButtonOffer[]? Buttons = null,
    ChatChartOffer[]? Charts = null,
    ChatTimerOffer[]? Timers = null,
    string? TurnId = null,
    string? Status = null);

