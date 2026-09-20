namespace DigitalBrain.Memory;

/// <summary>A note to remember.</summary>
[GenerateSerializer]
[Alias("memory.remember")]
public sealed record Remember(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key,
    [property: Id(2)] string Text,
    [property: Id(3)] IReadOnlyList<MemoryTag> Tags,
    [property: Id(4)] ProtectedPayloadReference? Payload);
