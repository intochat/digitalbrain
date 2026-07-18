using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Infrastructure.Services;

public class SubscriptionPolicy : ISubscriptionPolicy
{
    public bool IsEligibleForScheduledExecutions(User user) => PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user);
}
