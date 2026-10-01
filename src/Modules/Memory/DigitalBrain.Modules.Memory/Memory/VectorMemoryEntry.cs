namespace DigitalBrain.Memory;

[GenerateSerializer, Alias("memory.vector-entry")]
internal sealed record VectorMemoryEntry(
    [property: Id(0)] string Name,
    [property: Id(1)] string Namespace,
    [property: Id(2)] string Key,
    [property: Id(3)] string Text,
    [property: Id(4)] IReadOnlyList<MemoryTag> Tags,
    [property: Id(5)] ProtectedPayloadReference? Payload,
    [property: Id(6)] float[] Embedding);
