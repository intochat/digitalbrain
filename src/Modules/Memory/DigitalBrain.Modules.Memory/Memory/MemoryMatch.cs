namespace DigitalBrain.Memory;

[GenerateSerializer, Alias("memory.match")]
internal sealed record MemoryMatch([property: Id(0)] RecalledMemory Note, [property: Id(1)] double Score);
