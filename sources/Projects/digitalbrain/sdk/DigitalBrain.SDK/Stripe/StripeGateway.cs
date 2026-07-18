using System.Globalization;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace DigitalBrain.SDK.Stripe;

internal sealed class StripeGateway(IConfiguration configuration, ILogger<StripeGateway> logger) : IStripeGateway
{
    private const string CompletedEventType = "checkout.session.completed";
    private const string BundleMetadataKey = "BundleId";
    private const string UserMetadataKey = "UserId";

    private string? SecretKey => configuration["Stripe:SecretKey"];
    private string? WebhookSecret => configuration["Stripe:WebhookSecret"];

    public async Task<StripeCheckoutSession> CreateCheckoutSessionAsync(StripeCheckoutRequest request, CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string>
        {
            [BundleMetadataKey] = request.BundleId,
            [UserMetadataKey] = request.UserId,
        };

        var secretKey = SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            // No live key: synthesize a deterministic session so dev/test buy flows still
            // return a URL/id without contacting Stripe. The buyer gets no license here.
            var syntheticId = $"cs_test_{Guid.NewGuid():N}";
            logger.LogInformation(
                "Stripe: no secret key configured — returning synthetic checkout session {SessionId} for bundle {BundleId}.",
                syntheticId, request.BundleId);
            return new StripeCheckoutSession(syntheticId, $"https://checkout.stripe.com/c/pay/{syntheticId}");
        }

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.UserId,
            Metadata = metadata,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = ToMinorUnits(request.Price),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = request.ProductName,
                            Metadata = metadata,
                        },
                    },
                },
            ],
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, new RequestOptions { ApiKey = secretKey }, ct);
        logger.LogInformation("Stripe: created live checkout session {SessionId} for bundle {BundleId}.", session.Id, request.BundleId);
        return new StripeCheckoutSession(session.Id, session.Url);
    }

    public StripeWebhookEvent VerifyEvent(string payloadJson, string? signatureHeader)
    {
        Event stripeEvent;
        var webhookSecret = WebhookSecret;

        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            try
            {
                stripeEvent = EventUtility.ConstructEvent(payloadJson, signatureHeader, webhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe: webhook signature verification failed.");
                return new StripeWebhookEvent(Ok: false, Reason: "signature verification failed", EventId: "", EventType: "", SessionId: null, BundleId: null, UserId: null);
            }
        }
        else
        {
            // No secret to validate against, so parse without verifying. This is a dev/test
            // convenience — a production brain MUST set Stripe:WebhookSecret or it will accept
            // forged completion events. Warn loudly so a misconfigured deployment is visible.
            logger.LogWarning("Stripe: no Stripe:WebhookSecret configured — accepting webhook event WITHOUT signature verification. Do not run this in production.");
            stripeEvent = EventUtility.ParseEvent(payloadJson, throwOnApiVersionMismatch: false);
        }

        string? sessionId = null;
        string? bundleId = null;
        string? userId = null;

        if (string.Equals(stripeEvent.Type, CompletedEventType, StringComparison.Ordinal)
            && stripeEvent.Data.Object is Session session)
        {
            sessionId = session.Id;
            session.Metadata?.TryGetValue(BundleMetadataKey, out bundleId);
            session.Metadata?.TryGetValue(UserMetadataKey, out userId);
            userId ??= session.ClientReferenceId;
        }

        return new StripeWebhookEvent(
            Ok: true,
            Reason: "ok",
            EventId: stripeEvent.Id ?? "",
            EventType: stripeEvent.Type ?? "",
            SessionId: sessionId,
            BundleId: bundleId,
            UserId: userId);
    }

    // Stripe takes amounts in the currency's minor unit (cents). "free" and unparsable
    // prices collapse to 0, which the caller never reaches for premium bundles.
    private static long ToMinorUnits(string price)
    {
        if (decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        }
        return 0;
    }
}
