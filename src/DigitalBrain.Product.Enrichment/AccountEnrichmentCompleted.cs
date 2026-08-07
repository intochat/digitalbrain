namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// A prepared, approved enrichment was confirmed by the Salesforce effect boundary.
/// </summary>
public sealed record AccountEnrichmentCompleted : Synapse
{
    public AccountEnrichmentCompleted(string runId, string mutationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutationId);
        RunId = runId.Trim();
        MutationId = mutationId.Trim();
    }

    public string RunId { get; }

    public string MutationId { get; }
}
