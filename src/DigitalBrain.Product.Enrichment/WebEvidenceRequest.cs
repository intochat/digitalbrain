namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Input supplied to a web research evidence adapter.
/// </summary>
public sealed record WebEvidenceRequest
{
    public WebEvidenceRequest(AccountEnrichmentRequest enrichment)
    {
        Enrichment = enrichment ?? throw new ArgumentNullException(nameof(enrichment));
    }

    public AccountEnrichmentRequest Enrichment { get; }
}
