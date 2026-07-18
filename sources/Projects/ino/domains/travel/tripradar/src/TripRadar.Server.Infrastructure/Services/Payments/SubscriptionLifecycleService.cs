using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Services.Payments.Internal;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class SubscriptionLifecycleService(
    IStripeGateway stripeGateway,
    SubscriptionRecordService subscriptionRecordService,
    DeferredDowngradeService deferredDowngradeService,
    ILogger<SubscriptionLifecycleService> logger)
    : ISubscriptionLifecycleService
{
    public async Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptionResult = await subscriptionRecordService.GetExistingAsync(user, cancellationToken);
            if (subscriptionResult.IsFailure)
            {
                return Result.Failure(subscriptionResult.Error);
            }

            var userSubscription = subscriptionResult.Value!;
            var stripeSubscription = await stripeGateway.ToggleSubscriptionAsync(userSubscription.StripeSubscriptionId!, activate: false, cancellationToken);

            if (string.Equals(stripeSubscription.Status, SubscriptionConstants.Status.Active, StringComparison.OrdinalIgnoreCase)
                && stripeSubscription.CurrentPeriodEnd > DateTime.UtcNow)
            {
                var expirationTime = DateTime.SpecifyKind(stripeSubscription.CurrentPeriodEnd, DateTimeKind.Utc);
                return await deferredDowngradeService.ScheduleAsync(user, userSubscription, UserTierType.Basic.Id, expirationTime, cancellationToken);
            }

            return await subscriptionRecordService.DowngradeToBasicImmediatelyAsync(user, userSubscription, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error canceling subscription for user {UserId}", user.Id);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    public async Task<Result> DowngradeSubscriptionAsync(User user, int targetLowerTierId, int billingPeriodId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userSubscription = await subscriptionRecordService.GetByUserIdAsync(user.Id, cancellationToken);
            if (userSubscription is null || string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId))
            {
                return await subscriptionRecordService.UpdateTierWithoutSubscriptionAsync(user, targetLowerTierId, cancellationToken);
            }

            var targetPriceResult = await subscriptionRecordService.GetRequiredPriceAsync(targetLowerTierId, billingPeriodId, cancellationToken);
            if (targetPriceResult.IsFailure)
            {
                return Result.Failure(targetPriceResult.Error);
            }

            var (status, _, currentPeriodEnd) = await stripeGateway.GetSubscriptionDetailsAsync(userSubscription.StripeSubscriptionId, cancellationToken);
            await stripeGateway.UpdateSubscriptionPriceAsync(userSubscription.StripeSubscriptionId, targetPriceResult.Value!.StripeId!, cancellationToken);

            if (status != SubscriptionConstants.Status.Active || !currentPeriodEnd.HasValue)
            {
                return await subscriptionRecordService.UpdateTierWithoutSubscriptionAsync(user, targetLowerTierId, cancellationToken);
            }

            return await deferredDowngradeService.ScheduleAsync(user, userSubscription, targetLowerTierId, currentPeriodEnd.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downgrading subscription for user {UserId}", user.Id);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    public async Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await subscriptionRecordService.ApplyDeferredDowngradeAsync(user, targetTierId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during deferred downgrade for user {UserId} to tier {TargetTierId}", user.Id, targetTierId);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    public async Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            return await subscriptionRecordService.UpdatePayAsYouGoAsync(user, enabled, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating PAYG setting for user {UserId}", user.Id);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }
}
