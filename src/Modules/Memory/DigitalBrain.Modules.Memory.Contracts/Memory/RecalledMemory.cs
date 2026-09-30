namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.recalled-memory")]
public sealed record RecalledMemory(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyList<MemoryTag> Tags,
    [property: Id(3)] ProtectedPayloadReference? Payload);
