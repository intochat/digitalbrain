using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class DiscountType : Entity<int>
{
    private DiscountType() { }

    public DiscountType(int id, string name, string? description)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedAt = DateTime.UtcNow;
    }

    public new int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    // Use only in EF
    private ICollection<PromoCode> PromoCodes { get; set; } = new List<PromoCode>();
}
