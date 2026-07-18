using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Extensions;

public static class StripeSubscriptionInfoExtensions
{
    public static SubscriptionStatusType GetStatusEnum(this StripeSubscriptionInfo? subscription)
    {
        if (subscription?.Status is null)
            return SubscriptionStatusType.Unknown;

        return subscription.Status.Trim().ToLowerInvariant() switch
        {
            "active" => SubscriptionStatusType.Active,
            "trialing" => SubscriptionStatusType.Trialing,
            "past_due" => SubscriptionStatusType.PastDue,
            "canceled" => SubscriptionStatusType.Canceled,
            "unpaid" => SubscriptionStatusType.Unpaid,
            "incomplete" => SubscriptionStatusType.Incomplete,
            "incomplete_expired" => SubscriptionStatusType.IncompleteExpired,
            "paused" => SubscriptionStatusType.Paused,
            _ => SubscriptionStatusType.Unknown
        };
    }
}
