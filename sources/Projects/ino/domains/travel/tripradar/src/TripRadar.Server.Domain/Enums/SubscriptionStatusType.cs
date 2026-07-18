using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class SubscriptionStatusType(int id, string name) : Enumeration(id, name)
{
    public static readonly SubscriptionStatusType Unknown = new(0, nameof(Unknown));
    public static readonly SubscriptionStatusType Active = new(1, nameof(Active));
    public static readonly SubscriptionStatusType Trialing = new(2, nameof(Trialing));
    public static readonly SubscriptionStatusType PastDue = new(3, nameof(PastDue));
    public static readonly SubscriptionStatusType Canceled = new(4, nameof(Canceled));
    public static readonly SubscriptionStatusType Unpaid = new(5, nameof(Unpaid));
    public static readonly SubscriptionStatusType Incomplete = new(6, nameof(Incomplete));
    public static readonly SubscriptionStatusType IncompleteExpired = new(7, nameof(IncompleteExpired));
    public static readonly SubscriptionStatusType Paused = new(8, nameof(Paused));
}

