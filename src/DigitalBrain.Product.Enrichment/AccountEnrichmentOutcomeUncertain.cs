namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// A run stopped without a confirmed change. Details remain inside the provider boundary.
/// </summary>
public sealed record AccountEnrichmentOutcomeUncertain : Synapse
{
    public AccountEnrichmentOutcomeUncertain(string runId, string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        RunId = runId.Trim();
        Stage = stage.Trim();
    }

    public string RunId { get; }

    public string Stage { get; }
}
