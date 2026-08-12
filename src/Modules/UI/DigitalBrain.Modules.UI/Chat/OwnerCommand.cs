using DigitalBrain.Abstractions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[GenerateSerializer]
internal sealed record OwnerCommand(
    [property: Id(0)] Guid CommandId,
    [property: Id(1)] string Text,
    [property: Id(2)] ActorContext? Actor = null);