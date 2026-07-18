using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Domain.Policies;

public static class OverageEligibilityPolicy
{
    public static bool IsEligible(User user, UserSubscription? subscription, bool requiresStripeSubscriptionId)
    {
        if (!PaidTierEligibilityPolicy.IsPaidTier(user) || subscription is null)
            return false;

        return subscription.CanUseOverage(requiresStripeSubscriptionId);
    }
}
