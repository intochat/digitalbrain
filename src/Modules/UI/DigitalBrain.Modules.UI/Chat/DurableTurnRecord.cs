using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record DurableTurnRecord(
    [property: Id(0)] Guid TurnId,
    [property: Id(1)] Guid CommandId,
    [property: Id(2)] string Text,
    [property: Id(3)] ActorContext Actor,
    [property: Id(4)] ChatTurnStatus Status,
    [property: Id(5)] long Revision);
