using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

// The responder's kernel work id lets a later cancellation stop its reaction.
[GenerateSerializer, Alias("db.ui.chat-turn-record")]
public sealed record ChatTurnRecord(
    [property: Id(0)] ChatTurnSnapshot Snapshot,
    [property: Id(1)] SignalId? ResponderWork,
    [property: Id(3)] NeuronId? Responder = null);
