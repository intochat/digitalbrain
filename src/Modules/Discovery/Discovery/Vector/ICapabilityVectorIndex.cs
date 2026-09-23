namespace DigitalBrain.Discovery.Vector;

internal sealed record CapabilityVectorRecord(string Id, string WorkspaceId, string Text, float[] Embedding);

internal sealed record CapabilityVectorMatch(string Id, double Score);

// Durable mirror of the catalog. The manifest index is rebuilt from source; this keeps vectors
// for workspace instances and user memories that have no manifest.
internal interface ICapabilityVectorIndex
{
    string EmbeddingModel { get; }

    Task UpsertAsync(CapabilityCollection collection, IReadOnlyList<CapabilityVectorRecord> records, CancellationToken cancellationToken);

    Task<IReadOnlyList<CapabilityVectorMatch>> SearchAsync(
        CapabilityCollection collection,
        string workspaceId,
        float[] queryEmbedding,
        int take,
        CancellationToken cancellationToken);
}
