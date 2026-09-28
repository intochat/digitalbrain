using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Expander.Signals;

[GenerateSerializer, Alias("ui.expander-changed")]
public sealed record ExpanderChanged([property: Id(0)] string Name, [property: Id(1)] bool Expanded) : Signal;