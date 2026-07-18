using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Domain.Aggregates;

public class Price : AggregateRoot<long>
{
    private Price()
    {
    }

    public Price(int tierId, long amount, int billingPeriodId, int currencyId, string? stripeId = null)
    {
        TierId = tierId;
        Amount = amount;
        BillingPeriodId = billingPeriodId;
        CurrencyId = currencyId;
        StripeId = stripeId;
        CreatedAt = DateTime.UtcNow;
    }

    public new long Id { get; private set; }
    public Tier Tier { get; private set; } = null!;
    public int TierId { get; private set; }

    public long Amount { get; private set; }

    public string? StripeId { get; private set; }

    public string? StripeIdHash { get; private set; }

    public BillingPeriod BillingPeriod { get; private set; } = null!;
    public int BillingPeriodId { get; private set; }

    public Currency Currency { get; private set; } = null!;
    public int CurrencyId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public void UpdateStripeIdHash(string? stripeIdHash)
    {
        StripeIdHash = string.IsNullOrWhiteSpace(stripeIdHash) ? null : stripeIdHash.Trim();
    }
}
