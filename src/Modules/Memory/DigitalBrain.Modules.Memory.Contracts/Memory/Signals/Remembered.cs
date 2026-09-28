using DigitalBrain.Contracts;

namespace DigitalBrain.Memory.Signals;

[GenerateSerializer, Alias("memory.remembered")]
public sealed record Remembered(
    [property: Id(0)] MemoryKey Key) : Signal;