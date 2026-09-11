namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.remembering-body")]
public sealed record RememberingBody(
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key,
    [property: Id(2)] string Text,
    [property: Id(3)] IReadOnlyList<MemoryTag> Tags,
    [property: Id(4)] ProtectedPayloadReference? Payload);
