using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class SubscriptionChangeType(int id, string name) : Enumeration(id, name)
{
    public static readonly SubscriptionChangeType NewSubscription = new(1, nameof(NewSubscription));
    public static readonly SubscriptionChangeType MonthlyToYearly = new(2, nameof(MonthlyToYearly));
    public static readonly SubscriptionChangeType YearlyToMonthly = new(3, nameof(YearlyToMonthly));
    public static readonly SubscriptionChangeType SameTierDifferentBilling = new(4, nameof(SameTierDifferentBilling));
    public static readonly SubscriptionChangeType TierUpgrade = new(5, nameof(TierUpgrade));
    public static readonly SubscriptionChangeType TierDowngrade = new(6, nameof(TierDowngrade));
    public static readonly SubscriptionChangeType RegularUpdate = new(7, nameof(RegularUpdate));
}
