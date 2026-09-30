namespace DigitalBrain.Memory;

[GenerateSerializer, Alias("memory.deletion")]
internal sealed record MemoryDeletion([property: Id(0)] string Name, [property: Id(1)] string Namespace);
