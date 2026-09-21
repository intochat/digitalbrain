using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Color.Signals;

[GenerateSerializer, Alias("ui.color-changed")]
public sealed record ColorChanged([property: Id(0)] string Name, [property: Id(1)] string Hex) : Signal;