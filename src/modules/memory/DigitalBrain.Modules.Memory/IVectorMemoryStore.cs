using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

internal interface IVectorMemoryStore
{
    void Upsert(VectorMemoryEntry entry);

    IReadOnlyList<VectorMemoryMatch> Search(
        string owner,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter);

    bool Remove(string owner, string @namespace, string key);
}

internal sealed record VectorMemoryEntry(
    string Owner,
    string Namespace,
    string Key,
    string Text,
    IReadOnlyDictionary<string, string> Metadata,
    ProtectedPayloadReference? Payload,
    float[] Embedding);
