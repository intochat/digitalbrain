using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.search-vector")]
public sealed record SearchVectorMemory(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] string Query,
    [property: Id(2)] int Limit,
    [property: Id(3)] IReadOnlyDictionary<string, string>? Metadata) : RequestSynapse<VectorMemoryMatches>;

[GenerateSerializer]
[Alias("memory.vector-matches")]
public sealed record VectorMemoryMatches(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] IReadOnlyList<VectorMemoryMatch> Matches) : Synapse;

[GenerateSerializer]
[Alias("memory.vector-match")]
public sealed record VectorMemoryMatch(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyDictionary<string, string> Metadata,
    [property: Id(3)] ProtectedPayloadReference? Payload);
