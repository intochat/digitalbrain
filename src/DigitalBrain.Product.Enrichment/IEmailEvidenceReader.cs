namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Obtains account-relevant evidence from an email provider.
/// </summary>
public interface IEmailEvidenceReader
{
    Task<IReadOnlyList<EnrichmentEvidence>> ReadAsync(
        EmailEvidenceRequest request,
        CancellationToken cancellationToken);
}
