namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.key")]
public sealed record MemoryKey(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key);
