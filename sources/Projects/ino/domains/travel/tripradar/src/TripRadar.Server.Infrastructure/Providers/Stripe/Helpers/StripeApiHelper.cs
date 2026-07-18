using Stripe;
using Stripe.Checkout;
using TripRadar.Server.Infrastructure.Providers.Stripe.Settings;

namespace TripRadar.Server.Infrastructure.Providers.Stripe.Helpers;

public static class StripeApiHelper
{
    public const string DefaultRefundReason = "requested_by_customer";
    public const string DefaultUsage = "off_session";
    public const string CardPaymentMethod = "card";
    public const string PaidInvoiceStatus = "paid";
    public const string MeteredUsageType = "metered";
    public const string ActiveSubscriptionStatus = "active";

    private const string SubscriptionMode = "subscription";
    private static readonly HashSet<string> _validRefundReasons = ["duplicate", "fraudulent", "requested_by_customer"];

    public static string ValidateAndNormalizeRefundReason(string reason) =>
        _validRefundReasons.Contains(reason.ToLowerInvariant()) ? reason.ToLowerInvariant() : DefaultRefundReason;

    public static SessionCreateOptions CreateCheckoutSessionOptions(string customerId, string priceId, StripeApiSettings settings) =>
        new()
        {
            Customer = customerId,
            PaymentMethodTypes = [CardPaymentMethod],
            LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
            Mode = SubscriptionMode,
            SuccessUrl = settings.SuccessUrl,
            CancelUrl = settings.CancelUrl,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = CreateServiceMetadata(settings) }
        };

    public static CustomerCreateOptions CreateCustomerOptions(string email, string? name, StripeApiSettings settings) =>
        new()
        {
            Email = email,
            Name = name,
            Description = settings.Description,
            Metadata = CreateServiceMetadata(settings)
        };

    public static RefundCreateOptions CreateRefundOptions(string paymentIntentId, int? amount, string reason, Dictionary<string, string>? metadata, StripeApiSettings settings) =>
        new()
        {
            PaymentIntent = paymentIntentId,
            Amount = amount,
            Reason = reason,
            Metadata = metadata ?? CreateDefaultRefundMetadata(settings)
        };

    private static Dictionary<string, string> CreateServiceMetadata(StripeApiSettings settings) =>
        new()
        {
            { "service", settings.ServiceName }, { "type", settings.SubscriptionType }
        };

    private static Dictionary<string, string> CreateDefaultRefundMetadata(StripeApiSettings settings) =>
        new()
        {
            { "service", settings.ServiceName }, { "created_by", "internal_system" }
        };
}
