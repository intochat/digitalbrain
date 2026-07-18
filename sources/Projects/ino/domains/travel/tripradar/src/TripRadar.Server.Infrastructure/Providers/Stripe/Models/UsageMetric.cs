namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Usage metric for a specific resource.
/// </summary>
public class UsageMetric
{
    /// <summary>Current usage amount.</summary>
    public long Current { get; init; }

    /// <summary>Usage limit (-1 for unlimited).</summary>
    public long Limit { get; init; }

    /// <summary>Usage percentage (0-100).</summary>
    public decimal Percentage { get; init; }

    /// <summary>Date when usage resets (UTC).</summary>
    public DateTime ResetDate { get; init; }
}
