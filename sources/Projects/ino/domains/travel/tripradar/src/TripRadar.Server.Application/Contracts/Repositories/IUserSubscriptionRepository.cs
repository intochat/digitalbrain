using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUserSubscriptionRepository
{
    Task<UserSubscription?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
    Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId, CancellationToken cancellationToken = default);
    Task<UserSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken cancellationToken = default);
    Task<UserSubscription> CreateAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default);
    Task UpdateAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default);
    Task UpdateDeferredDowngradeJobIdAsync(long userId, string? jobId, CancellationToken cancellationToken = default);
    Task<string?> GetDeferredDowngradeJobIdAsync(long userId, CancellationToken cancellationToken = default);
}
