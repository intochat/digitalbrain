using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Graph.Signals;

[GenerateSerializer, Alias("ui.graph-changed")]
public sealed record GraphChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;