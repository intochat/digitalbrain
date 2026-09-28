using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Person.Signals;

[GenerateSerializer, Alias("ui.person-changed")]
public sealed record PersonChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;