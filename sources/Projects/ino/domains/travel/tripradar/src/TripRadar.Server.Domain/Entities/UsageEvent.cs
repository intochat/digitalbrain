using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class UsageEvent : Entity<long>
{
    private UsageEvent()
    {
    }

    public UsageEvent(
        long userId,
        int serviceTypeId,
        int usageEventSourceId,
        decimal tokensConsumed,
        DateTime occurredAt,
        long? tripVaultId = null)
    {
        UniqueId = Guid.NewGuid();
        UserId = userId;
        ServiceTypeId = serviceTypeId;
        UsageEventSourceId = usageEventSourceId;
        TokensConsumed = tokensConsumed;
        TripVaultId = tripVaultId;
        OccurredAt = occurredAt.Kind == DateTimeKind.Utc ? occurredAt : DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc);
        CreatedAt = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public Guid UniqueId { get; private set; }

    public long UserId { get; private set; }

    public User User { get; private set; } = null!;

    public int ServiceTypeId { get; private set; }

    public ServiceType ServiceType { get; private set; } = null!;

    public long? TripVaultId { get; private set; }

    public TripVault? TripVault { get; private set; }

    public int UsageEventSourceId { get; private set; }

    public UsageEventSource UsageEventSource { get; private set; } = null!;

    public decimal TokensConsumed { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
}
