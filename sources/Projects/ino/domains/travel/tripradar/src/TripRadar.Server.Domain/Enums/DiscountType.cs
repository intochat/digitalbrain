using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class DiscountType(int id, string name) : Enumeration(id, name)
{
    public static readonly DiscountType Percentage = new(1, nameof(Percentage));
    public static readonly DiscountType FixedAmount = new(2, nameof(FixedAmount));
}