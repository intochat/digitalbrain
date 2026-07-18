using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class TokenConsumptionType(int id, string name) : Enumeration(id, name)
{
    public static readonly TokenConsumptionType Tier = new(1, nameof(Tier));
    public static readonly TokenConsumptionType Overage = new(2, nameof(Overage));
}
