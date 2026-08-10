using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.store-vector")]
public sealed record StoreVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Key,
    [property: Id(2)] string Text,
    [property: Id(3)] IReadOnlyDictionary<string, string>? Metadata,
    [property: Id(4)] ProtectedPayloadReference? Payload) : RequestSynapse<VectorMemoryStored>;

[GenerateSerializer]
[Alias("memory.vector-stored")]
public sealed record VectorMemoryStored(
    [property: Id(0)] bool Stored,
    [property: Id(1)] VectorMemoryNamespace Namespace,
    [property: Id(2)] string Key,
    [property: Id(3)] VectorMemoryStoreStatus Status) : Synapse;
