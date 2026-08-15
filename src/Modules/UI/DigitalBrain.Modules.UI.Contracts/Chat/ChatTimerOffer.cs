namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chat-timer-offer")]
public sealed record ChatTimerOffer(
    [property: Id(0)] string Label,
    [property: Id(1)] DateTimeOffset DueAt);
