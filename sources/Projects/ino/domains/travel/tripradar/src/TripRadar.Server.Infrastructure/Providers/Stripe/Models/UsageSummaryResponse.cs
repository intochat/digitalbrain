namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Metered usage summary from Stripe.
/// </summary>
public class UsageSummaryResponse
{
    /// <summary>Current billing period information.</summary>
    public required BillingPeriod CurrentPeriod { get; init; }

    /// <summary>Usage metrics keyed by resource type (e.g., "apiCalls", "storage").</summary>
    public Dictionary<string, UsageMetric> Usage { get; init; } = [];

    /// <summary>Subscription ID being tracked.</summary>
    public string? SubscriptionId { get; init; }

    /// <summary>Whether the subscription has metered billing enabled.</summary>
    public bool HasMeteredBilling { get; init; }
}
