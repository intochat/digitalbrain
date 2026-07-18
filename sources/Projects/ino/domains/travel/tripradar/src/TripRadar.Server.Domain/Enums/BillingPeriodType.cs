using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class BillingPeriodType(int id, string name) : Enumeration(id, name)
{
    public static readonly BillingPeriodType Monthly = new(1, nameof(Monthly));
    public static readonly BillingPeriodType Yearly = new(2, nameof(Yearly));
}
