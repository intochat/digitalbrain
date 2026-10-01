using DigitalBrain.Contracts;

namespace DigitalBrain.Memory.Signals;

[GenerateSerializer, Alias("memory.forgotten")]
public sealed record Forgotten(
    [property: Id(0)] MemoryKey Key) : Signal;
