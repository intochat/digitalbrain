using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Domain.Entities;

public class OveragePricing : Entity<int>
{
    private OveragePricing()
    {
    }

    public int TierId { get; private set; }

    public decimal PricePerToken { get; private set; }

    public int CurrencyId { get; private set; }

    public Currency Currency { get; private set; } = null!;

    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
}
