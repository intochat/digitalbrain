using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.runtime-signal")]
public sealed record AppSignal([property: Id(0)] string Source, [property: Id(1)] string Name, [property: Id(2)] string Value) : Signal;
