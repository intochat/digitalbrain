using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Domain.Entities;

public class OverageBillingRecord : Entity<long>
{
    private OverageBillingRecord()
    {
    }

    public OverageBillingRecord(
        long userId,
        Enums.ServiceType serviceType,
        decimal overageTokensUsed,
        decimal tokenUnitCost,
        Currency currency,
        string? metadata = null)
    {
        UserId = userId;
        ServiceTypeId = serviceType.Id;
        OverageTokensUsed = overageTokensUsed;
        TokenUnitCost = tokenUnitCost;
        TotalCharge = Math.Round(overageTokensUsed * tokenUnitCost, 2, MidpointRounding.AwayFromZero);
        Currency = currency;

        var now = DateTime.UtcNow;
        Year = now.Year;
        Month = now.Month;
        UsageTimestamp = now;
        CreatedAt = now;

        IsBilled = false;
        Metadata = metadata;
    }

    public OverageBillingRecord(
        long userId,
        Enums.ServiceType serviceType,
        decimal overageTokensUsed,
        decimal tokenUnitCost,
        int currencyId,
        string? metadata = null)
    {
        UserId = userId;
        ServiceTypeId = serviceType.Id;
        OverageTokensUsed = overageTokensUsed;
        TokenUnitCost = tokenUnitCost;
        TotalCharge = Math.Round(overageTokensUsed * tokenUnitCost, 2, MidpointRounding.AwayFromZero);
        CurrencyId = currencyId;

        var now = DateTime.UtcNow;
        Year = now.Year;
        Month = now.Month;
        UsageTimestamp = now;
        CreatedAt = now;

        IsBilled = false;
        Metadata = metadata;
    }

    public new long Id { get; private set; }

    public long UserId { get; private set; }

    public User User { get; private set; } = null!;

    public ServiceType ServiceType { get; private set; } = null!;

    public int ServiceTypeId { get; private set; }

    public decimal OverageTokensUsed { get; private set; }

    public decimal TokenUnitCost { get; private set; }

    public decimal TotalCharge { get; private set; }

    public int CurrencyId { get; set; }

    public Currency Currency { get; set; } = null!;

    public int Year { get; private set; }

    public int Month { get; private set; }

    public DateTime UsageTimestamp { get; private set; }

    public bool IsBilled { get; private set; }

    public DateTime? BilledAt { get; private set; }

    public string? StripeInvoiceId { get; private set; }

    public string? Metadata { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Used to prevent race conditions in billing. Set when a process is actively billing these records.
    /// </summary>
    public string? ProcessingId { get; private set; }

    /// <summary>
    /// Timestamp when processing started. Used to detect stale locks.
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }
}

