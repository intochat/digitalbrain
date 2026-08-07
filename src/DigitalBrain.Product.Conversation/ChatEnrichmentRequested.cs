using DigitalBrain.Product.Enrichment;

namespace DigitalBrain.Product.Conversation;

/// <summary>
/// Public chat intent already resolved into a typed account-enrichment request.
/// </summary>
public sealed record ChatEnrichmentRequested : Synapse
{
    public ChatEnrichmentRequested(AccountEnrichmentRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public AccountEnrichmentRequest Request { get; }
}
