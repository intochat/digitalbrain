using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.button-clicked")]
public sealed record ButtonClicked(
    [property: Id(0)] CommandId OfferCommandId,
    [property: Id(1)] string ButtonId,
    [property: Id(2)] string Action) : Synapse;
