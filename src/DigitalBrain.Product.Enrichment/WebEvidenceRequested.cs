namespace DigitalBrain.Product.Enrichment;

public sealed record WebEvidenceRequested : Synapse
{
    public WebEvidenceRequested(AccountEnrichmentRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AccountEnrichmentRequest Request { get; }
}
