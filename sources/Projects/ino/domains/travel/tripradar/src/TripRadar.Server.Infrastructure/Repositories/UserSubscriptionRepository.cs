using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class UserSubscriptionRepository(TripRadarDbContext context, IMapper mapper) : IUserSubscriptionRepository
{
    public async Task<UserSubscription?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var userSubscription = await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.Where(s => s.UserId == userId && s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        return userSubscription is not null ? mapper.Map<UserSubscription>(userSubscription) : null;
    }

    public async Task<UserSubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var userSubscription = await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.Where(s => s.StripeSubscriptionId == stripeSubscriptionId && s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        return userSubscription is not null ? mapper.Map<UserSubscription>(userSubscription) : null;
    }

    public async Task<UserSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId,
        CancellationToken cancellationToken = default)
    {
        var userSubscription = await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.Where(s => s.StripeCustomerId == stripeCustomerId && s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        return userSubscription is not null ? mapper.Map<UserSubscription>(userSubscription) : null;
    }

    public async Task<UserSubscription> CreateAsync(UserSubscription userSubscription,
        CancellationToken cancellationToken = default)
    {
        var existingSubscription = await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.Where(s => s.UserId == userSubscription.UserId && s.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        if (existingSubscription is not null)
        {
            return mapper.Map<UserSubscription>(existingSubscription);
        }

        var result = await context.UserSubscriptions.AddAsync(userSubscription, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return mapper.Map<UserSubscription>(result.Entity);
    }

    public async Task UpdateAsync(UserSubscription userSubscription, CancellationToken cancellationToken = default)
    {
        var existingSubscription = await context.UserSubscriptions
            .FirstOrDefaultAsync(s => s.Id == userSubscription.Id, cancellationToken);

        if (existingSubscription is not null)
        {
            mapper.Map(userSubscription, existingSubscription);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateDeferredDowngradeJobIdAsync(long userId, string? jobId, CancellationToken cancellationToken = default)
    {
        var preferredSubscriptionId = await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.Where(s => s.UserId == userId && s.IsActive))
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (preferredSubscriptionId == 0)
        {
            return;
        }

        await context.UserSubscriptions
            .Where(s => s.Id == preferredSubscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeferredDowngradeJobId, jobId), cancellationToken);
    }

    public async Task<string?> GetDeferredDowngradeJobIdAsync(long userId, CancellationToken cancellationToken = default) =>
        await BuildPreferredActiveSubscriptionQuery(
                context.UserSubscriptions.AsNoTracking().Where(s => s.UserId == userId && s.IsActive))
            .Select(s => s.DeferredDowngradeJobId)
            .FirstOrDefaultAsync(cancellationToken);

    private static IOrderedQueryable<UserSubscription> BuildPreferredActiveSubscriptionQuery(IQueryable<UserSubscription> query) =>
        query
            .OrderByDescending(s => !string.IsNullOrWhiteSpace(s.StripeSubscriptionId))
            .ThenByDescending(s => !string.IsNullOrWhiteSpace(s.StripeCustomerId))
            .ThenByDescending(s => s.SubscriptionExpirationTime.HasValue)
            .ThenByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .ThenByDescending(s => s.Id);
}
