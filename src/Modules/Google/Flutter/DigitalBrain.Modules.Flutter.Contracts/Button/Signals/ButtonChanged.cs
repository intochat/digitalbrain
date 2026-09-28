using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Button.Signals;

[GenerateSerializer, Alias("ui.button-changed")]
public sealed record ButtonChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;