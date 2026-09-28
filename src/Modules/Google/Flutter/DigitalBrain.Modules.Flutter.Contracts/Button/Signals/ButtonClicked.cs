using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Button.Signals;

[GenerateSerializer, Alias("ui.button-clicked")]
public sealed record ButtonClicked([property: Id(0)] string Name, [property: Id(1)] string Action) : Signal;