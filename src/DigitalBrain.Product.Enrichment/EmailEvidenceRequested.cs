namespace DigitalBrain.Product.Enrichment;

public sealed record EmailEvidenceRequested : Synapse
{
    public EmailEvidenceRequested(AccountEnrichmentRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AccountEnrichmentRequest Request { get; }
}
