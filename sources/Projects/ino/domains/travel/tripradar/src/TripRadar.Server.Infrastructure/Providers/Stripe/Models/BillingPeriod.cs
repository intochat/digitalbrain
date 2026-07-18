namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Current billing period information.
/// </summary>
public class BillingPeriod
{
    /// <summary>Period start date (UTC).</summary>
    public DateTime Start { get; init; }

    /// <summary>Period end date (UTC).</summary>
    public DateTime End { get; init; }

    /// <summary>Days remaining in the current period.</summary>
    public int DaysRemaining { get; init; }
}
