using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class SubscriptionEventType(int id, string name) : Enumeration(id, name)
{
    public static readonly SubscriptionEventType SubscriptionCreated = new(1, "customer.subscription.created");
    public static readonly SubscriptionEventType SubscriptionDeleted = new(2, "customer.subscription.deleted");
    public static readonly SubscriptionEventType SubscriptionCanceled = new(3, "customer.subscription.canceled");
    public static readonly SubscriptionEventType SubscriptionUpdated = new(4, "customer.subscription.updated");
}
