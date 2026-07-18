using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services.Payments.Internal;

public sealed class SubscriptionRecordService(
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository)
{
    public Task<UserSubscription?> GetByUserIdAsync(long userId, CancellationToken cancellationToken) =>
        userSubscriptionRepository.GetByUserIdAsync(userId, cancellationToken);

    public async Task<Result<UserSubscription>> GetExistingAsync(User user, CancellationToken cancellationToken)
    {
        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
        return userSubscription is null || string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId)
            ? Result.Failure<UserSubscription>(Errors.PaymentNotFound)
            : Result.Success(userSubscription);
    }

    public async Task<Result<Price>> GetRequiredPriceAsync(int tierId, int billingPeriodId, CancellationToken cancellationToken)
    {
        var targetPrice = await unitOfWork.PriceRepository.GetByTierIdAndBillingPeriodAsync(tierId, billingPeriodId, cancellationToken);
        if (targetPrice is null || string.IsNullOrWhiteSpace(targetPrice.StripeId))
        {
            return Result.Failure<Price>(Errors.TierPriceNotFound);
        }

        return Result.Success(targetPrice);
    }

    public async Task<Result> DowngradeToBasicImmediatelyAsync(User user, UserSubscription userSubscription, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        user.UpdateTier(UserTierType.Basic.Id);
        userSubscription.UpdateStripeSubscriptionId(null);
        userSubscription.UpdateDeferredDowngrade(null, null);
        await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UpdateTierWithoutSubscriptionAsync(User user, int targetTierId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        user.UpdateTier(targetTierId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ApplyDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        user.UpdateTier(targetTierId);
        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (userSubscription is not null)
        {
            userSubscription.UpdateDeferredDowngrade(null, null);
            await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        var userSubscription = await GetOrCreateAsync(user, cancellationToken);
        userSubscription.SetPayAsYouGo(enabled);
        await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<UserSubscription> GetOrCreateAsync(User user, CancellationToken cancellationToken)
    {
        var existingSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (existingSubscription is not null)
        {
            return existingSubscription;
        }

        var newSubscription = new UserSubscription(user);
        var createdSubscription = await userSubscriptionRepository.CreateAsync(newSubscription, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return createdSubscription;
    }
}
