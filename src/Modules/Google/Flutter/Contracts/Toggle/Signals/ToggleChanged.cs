using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Toggle.Signals;

[GenerateSerializer, Alias("ui.toggle-changed")]
public sealed record ToggleChanged([property: Id(0)] string Name, [property: Id(1)] bool On) : Signal;