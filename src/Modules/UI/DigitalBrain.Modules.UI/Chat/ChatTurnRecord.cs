using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

// The responder's kernel work id lets a later cancellation stop its reaction.
[GenerateSerializer, Alias("db.ui.chat-turn-record")]
public sealed record ChatTurnRecord(
    [property: Id(0)] ChatTurnSnapshot Snapshot,
    [property: Id(1)] SignalId? ResponderWork,
    // Terminal and announced (Responded with an answer, TurnFailed otherwise); retries distinguish already settled from settled but never announced.
    [property: Id(2)] bool SettlementFired);
