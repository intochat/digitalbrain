namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Confirms that an enrichment run was durably accepted by its run behavior.
/// </summary>
public sealed record AccountEnrichmentRunAccepted : Synapse
{
    public AccountEnrichmentRunAccepted(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunId = runId.Trim();
    }

    public string RunId { get; }
}
