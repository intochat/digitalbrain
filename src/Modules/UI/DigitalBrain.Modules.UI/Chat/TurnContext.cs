using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.turn-context")]
public sealed record TurnContext(
    [property: Id(0)] SignalId Turn,
    [property: Id(1)] IReadOnlyList<ContextSlot> Slots);
