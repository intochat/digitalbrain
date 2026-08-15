namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chat-button-offer")]
public sealed record ChatButtonOffer(
    [property: Id(0)] string ButtonId,
    [property: Id(1)] string Label,
    [property: Id(2)] string Action);
