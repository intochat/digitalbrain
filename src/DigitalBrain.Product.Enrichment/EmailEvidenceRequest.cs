namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Input supplied to an email evidence adapter.
/// </summary>
public sealed record EmailEvidenceRequest
{
    public EmailEvidenceRequest(AccountEnrichmentRequest enrichment)
    {
        Enrichment = enrichment ?? throw new ArgumentNullException(nameof(enrichment));
    }

    public AccountEnrichmentRequest Enrichment { get; }
}
