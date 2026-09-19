using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter;

[GenerateSerializer, Alias("flutter.inbox-appeared")]
public sealed record InboxAppeared(
    [property: Id(0)] string InboxId,
    [property: Id(1)] string Text) : Signal;
