namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.tag")]
public sealed record MemoryTag(
    [property: Id(0)] string Name,
    [property: Id(1)] string Value);
