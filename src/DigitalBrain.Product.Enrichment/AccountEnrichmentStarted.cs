namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Starts a durable enrichment run at the run identity.
/// </summary>
public sealed record AccountEnrichmentStarted : Synapse
{
    public AccountEnrichmentStarted(AccountEnrichmentRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AccountEnrichmentRequest Request { get; }
}
