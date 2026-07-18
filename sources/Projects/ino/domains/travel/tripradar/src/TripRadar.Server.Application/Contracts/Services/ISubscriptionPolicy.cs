using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services;

public interface ISubscriptionPolicy
{
    bool IsEligibleForScheduledExecutions(User user);
}

