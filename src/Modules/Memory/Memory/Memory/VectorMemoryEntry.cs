namespace DigitalBrain.Memory;

internal sealed record VectorMemoryEntry(
    string Name,
    string Namespace,
    string Key,
    string Text,
    IReadOnlyList<MemoryTag> Tags,
    ProtectedPayloadReference? Payload,
    float[] Embedding);
