using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class RefundType(int id, string name) : Enumeration(id, name)
{
    public static readonly RefundType RequestedByCustomer = new(1, nameof(RequestedByCustomer));
    public static readonly RefundType Duplicate = new(2, nameof(Duplicate));
    public static readonly RefundType Fraudulent = new(3, nameof(Fraudulent));
    public static readonly RefundType SubscriptionCanceled = new(4, nameof(SubscriptionCanceled));
    public static readonly RefundType ServiceNotDelivered = new(5, nameof(ServiceNotDelivered));
}
