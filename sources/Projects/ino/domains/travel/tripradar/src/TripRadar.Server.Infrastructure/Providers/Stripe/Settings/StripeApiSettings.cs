namespace TripRadar.Server.Infrastructure.Providers.Stripe.Settings;

public class StripeApiSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PaymentMethodTypes { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string SubscriptionType { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}
