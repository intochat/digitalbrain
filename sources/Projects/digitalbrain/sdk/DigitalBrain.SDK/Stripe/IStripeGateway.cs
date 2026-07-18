namespace DigitalBrain.SDK.Stripe;

// The single Stripe connector facade: the one place Stripe.net is called. Both the
// marketplace checkout path and the webhook verification ride this so there is no
// second/forked Stripe integration. Offline (no secret key configured) it returns a
// synthetic session and skips signature checks so local/dev/test runs work without keys.
public interface IStripeGateway
{
    Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken ct = default);

    StripeWebhookEvent VerifyEvent(string payloadJson, string? signatureHeader);
}

public sealed record StripeCheckoutRequest(
    string BundleId,
    string UserId,
    string Price,
    string ProductName,
    string SuccessUrl,
    string CancelUrl);

public sealed record StripeCheckoutSession(string SessionId, string Url);

// Neutral, provider-agnostic projection of a verified Stripe event so the kernel never
// touches a Stripe.net type. Ok is false only when a webhook secret is configured and the
// signature fails to verify; with no secret (dev/test) the event is parsed and accepted.
public sealed record StripeWebhookEvent(
    bool Ok,
    string Reason,
    string EventId,
    string EventType,
    string? SessionId,
    string? BundleId,
    string? UserId);
