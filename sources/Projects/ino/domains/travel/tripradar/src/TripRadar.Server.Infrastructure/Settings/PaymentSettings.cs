namespace TripRadar.Server.Infrastructure.Settings;

public class PaymentSettings
{
    public const string SectionName = "PaymentSettings";

    public StripeSettings Stripe { get; set; } = new();
}
