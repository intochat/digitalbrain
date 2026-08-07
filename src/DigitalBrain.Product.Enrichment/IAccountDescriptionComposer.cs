namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Converts reviewed evidence into a proposed account description.
/// </summary>
public interface IAccountDescriptionComposer
{
    Task<AccountEnrichmentDraft> ComposeAsync(
        AccountEnrichmentRequest request,
        IReadOnlyList<EnrichmentEvidence> evidence,
        CancellationToken cancellationToken);
}
