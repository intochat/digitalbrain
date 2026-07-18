using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Domain.Policies;

public static class PaidTierEligibilityPolicy
{
    public static bool IsPaidTier(User? user) => user is not null && (user.TierId == UserTierType.Essential.Id || user.TierId == UserTierType.Advanced.Id);

    private static bool HasActiveSubscription(User? user, DateTime utcNow)
    {
        if (user?.UserSubscription is not { IsActive: true } subscription)
            return false;

        return subscription.SubscriptionExpirationTime is not { } expiration || expiration >= utcNow;
    }

    public static bool IsEligibleForPaidFeatures(User? user, DateTime? utcNow = null) => IsPaidTier(user) && HasActiveSubscription(user, utcNow ?? DateTime.UtcNow);
}
