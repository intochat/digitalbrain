namespace TripRadar.Server.Infrastructure.Settings;

public class StripeSettings
{
    public string SecretKey { get; set; } = string.Empty;

    public string PublishableKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool AllowUnverifiedWebhooksInDevelopment { get; set; }

    public string SuccessUrl { get; set; } = string.Empty;

    public string CancelUrl { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    public PriceSettings Prices { get; set; } = new();
}
