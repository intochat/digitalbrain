using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.TextField.Signals;

[GenerateSerializer, Alias("ui.textfield-changed")]
public sealed record TextFieldChanged([property: Id(0)] string Name, [property: Id(1)] string Value) : Signal;