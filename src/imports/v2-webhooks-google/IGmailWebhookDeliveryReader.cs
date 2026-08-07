using DigitalBrain.Product.Enrichment;
using DigitalBrain.Product.Webhooks;

namespace DigitalBrain.Product.Google;

/// <summary>
/// Turns an already-verified Gmail webhook receipt into a typed enrichment request, if relevant.
/// </summary>
public interface IGmailWebhookDeliveryReader
{
    /// <summary>
    /// Reads the delivery or reconciles a prior attempt. For one verified delivery identity,
    /// a non-null result must always describe the same enrichment request and run; a null
    /// result is terminally irrelevant for that delivery. Transient provider failures throw.
    /// </summary>
    Task<AccountEnrichmentRequest?> ReadOrReconcileAsync(
        WebhookDeliveryAccepted delivery,
        CancellationToken cancellationToken);
}
