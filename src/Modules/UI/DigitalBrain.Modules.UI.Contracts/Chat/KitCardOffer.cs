namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.kit-card")]
public sealed record KitCardOffer(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Name,
    [property: Id(2)] string Caption);
