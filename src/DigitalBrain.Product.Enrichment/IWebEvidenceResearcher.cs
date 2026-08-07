namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Obtains account-relevant evidence from the web.
/// </summary>
public interface IWebEvidenceResearcher
{
    Task<IReadOnlyList<EnrichmentEvidence>> ResearchAsync(
        WebEvidenceRequest request,
        CancellationToken cancellationToken);
}
