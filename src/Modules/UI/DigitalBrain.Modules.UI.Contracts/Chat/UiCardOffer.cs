namespace DigitalBrain.Chat;

[GenerateSerializer]
// Stable persisted Orleans identity, retained so existing card snapshots deserialize.
[Alias("ui.kit-card")]
public sealed record UiCardOffer(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Name,
    [property: Id(2)] string Caption);
