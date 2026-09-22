using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Map.Signals;

[GenerateSerializer, Alias("ui.map-changed")]
public sealed record MapChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;