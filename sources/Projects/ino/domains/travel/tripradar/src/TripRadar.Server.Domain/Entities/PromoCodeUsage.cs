using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class PromoCodeUsage : Entity<long>
{
    private PromoCodeUsage() {}

    public PromoCodeUsage(long promoCodeId, long userId, decimal discountApplied)
    {
        PromoCodeId =  promoCodeId;
        UserId = userId;
        DiscountApplied = discountApplied;
        UsedAt = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public long PromoCodeId { get; private set; }

    public long UserId { get; private set; }

    public DateTime UsedAt { get; private set; }

    public decimal DiscountApplied { get; private set; }

    // Navigation properties
    public PromoCode? PromoCode { get; private set; }
    public User? User { get; private set; }
}
