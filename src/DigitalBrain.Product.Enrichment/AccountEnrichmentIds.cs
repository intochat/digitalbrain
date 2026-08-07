namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Stable identities derived from an enrichment run.
/// </summary>
public static class AccountEnrichmentIds
{
    public static string ProposalIdOf(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return $"account-enrichment/{runId.Trim()}";
    }

    public static string MemoryEntryIdOf(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return $"account-enrichment-evidence/{runId.Trim()}";
    }
}
