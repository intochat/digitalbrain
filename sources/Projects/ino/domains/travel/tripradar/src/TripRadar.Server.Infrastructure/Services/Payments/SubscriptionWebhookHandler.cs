using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class SubscriptionWebhookHandler(
    IStripeGateway stripeGateway,
    ISubscriptionStateService stateService,
    ISubscriptionEmailService emailService,
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    ITierRepository tierRepository,
    IBackgroundJobService backgroundJobService,
    ILogger<SubscriptionWebhookHandler> logger) : ISubscriptionWebhookHandler
{
    public async Task<Result> ProcessEventAsync(string subscriptionId, SubscriptionEventType eventType, CancellationToken ct = default)
    {
        try
        {
            var contextResult = await LoadContextAsync(subscriptionId, eventType, ct);
            if (contextResult.IsFailure)
            {
                return Result.Failure(contextResult.Error);
            }

            return await DispatchEventAsync(contextResult.Value!, subscriptionId, eventType, ct);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected error processing subscription event {EventType} for subscription {SubscriptionId}",
                eventType.Name,
                subscriptionId);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    private async Task<Result<SubscriptionWebhookContext>> LoadContextAsync(string subscriptionId, SubscriptionEventType eventType, CancellationToken ct)
    {
        var userSubscription = await userSubscriptionRepository.GetByStripeSubscriptionIdAsync(subscriptionId, ct);
        if (userSubscription is null)
        {
            logger.LogError(
                "No user subscription found with subscription ID {SubscriptionId} for event {EventType}",
                subscriptionId,
                eventType.Name);
            return Result.Failure<SubscriptionWebhookContext>(Errors.UserNotFound);
        }

        var user = await unitOfWork.UserRepository.GetByIdAsync(userSubscription.UserId, ct);
        if (user is null)
        {
            logger.LogError(
                "No user found with ID {UserId} for subscription {SubscriptionId}",
                userSubscription.UserId,
                subscriptionId);
            return Result.Failure<SubscriptionWebhookContext>(Errors.UserNotFound);
        }

        return Result.Success(new SubscriptionWebhookContext(user, userSubscription));
    }

    private async Task<Result> DispatchEventAsync(SubscriptionWebhookContext context, string subscriptionId, SubscriptionEventType eventType, CancellationToken ct)
    {
        if (Equals(eventType, SubscriptionEventType.SubscriptionCreated))
        {
            return await HandleSubscriptionCreatedAsync(context, subscriptionId, ct);
        }

        if (Equals(eventType, SubscriptionEventType.SubscriptionDeleted) || Equals(eventType, SubscriptionEventType.SubscriptionCanceled))
        {
            return await HandleSubscriptionCanceledAsync(context, ct);
        }

        if (Equals(eventType, SubscriptionEventType.SubscriptionUpdated))
        {
            return await HandleSubscriptionUpdatedAsync(context, subscriptionId, ct);
        }

        return Result.Success();
    }

    private async Task<Result> HandleSubscriptionCreatedAsync(SubscriptionWebhookContext context, string subscriptionId, CancellationToken ct)
    {
        try
        {
            var subscriptionState = await TryGetActiveSubscriptionStateAsync(subscriptionId, ct);
            if (subscriptionState is null)
            {
                return Result.Success();
            }

            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: ct);

            if (!context.User.TierId.Equals(subscriptionState.Price.TierId))
            {
                context.User.UpdateTier(subscriptionState.Price.TierId);
            }

            if (subscriptionState.CurrentPeriodEnd.HasValue)
            {
                var expirationTime = DateTime.SpecifyKind(subscriptionState.CurrentPeriodEnd.Value, DateTimeKind.Utc);
                context.UserSubscription.UpdateSubscriptionExpirationTime(expirationTime);
                await backgroundJobService.CancelDeferredDowngradeAsync(context.User.Id, ct);
                context.UserSubscription.UpdatePendingTier(null);
            }

            await TryRefreshUserTokensOnPaymentAsync(context.User.Id, ct);
            await userSubscriptionRepository.UpdateAsync(context.UserSubscription, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await emailService.SendSubscriptionCreatedAsync(context.User, subscriptionState.Price, subscriptionState.CurrentPeriodEnd, ct);
            await scope.CommitAsync(ct);

            return Result.Success();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error handling subscription creation for user {UserId}", context.User.Id);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    private async Task<Result> HandleSubscriptionCanceledAsync(SubscriptionWebhookContext context, CancellationToken ct)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: ct);

        if (context.UserSubscription is { SubscriptionExpirationTime: not null, PendingTierId: not null })
        {
            context.UserSubscription.UpdateStripeSubscriptionId(null);
            await userSubscriptionRepository.UpdateAsync(context.UserSubscription, ct);
        }
        else
        {
            context.User.UpdateTier(UserTierType.Basic.Id);
            context.UserSubscription.UpdateStripeSubscriptionId(null);
            context.UserSubscription.UpdateDeferredDowngrade(null, null);
            await userSubscriptionRepository.UpdateAsync(context.UserSubscription, ct);
            await backgroundJobService.CancelDeferredDowngradeAsync(context.User.Id, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return Result.Success();
    }

    private async Task<Result> HandleSubscriptionUpdatedAsync(SubscriptionWebhookContext context, string subscriptionId, CancellationToken ct)
    {
        try
        {
            var subscriptionState = await TryGetActiveSubscriptionStateAsync(subscriptionId, ct);
            if (subscriptionState is null)
            {
                return Result.Success();
            }

            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: ct);

            var subscriptionChangeType = await stateService.DetermineChangeTypeAsync(
                context.User,
                subscriptionState.Price,
                context.UserSubscription,
                ct);

            logger.LogInformation("Processing subscription change for user {UserId}: {ChangeType}", context.User.Id, subscriptionChangeType);

            var oldTier = await tierRepository.GetByIdAsync(context.User.TierId, ct);
            var oldTierName = oldTier?.Name ?? "Unknown";

            if (ShouldDeferDowngrade(subscriptionChangeType, subscriptionState.CurrentPeriodEnd))
            {
                await ScheduleDeferredDowngradeAsync(context, subscriptionState.Price.TierId, subscriptionState.CurrentPeriodEnd!.Value, ct);
            }
            else
            {
                await ApplyImmediateSubscriptionUpdateAsync(context, subscriptionChangeType, subscriptionState.Price, subscriptionState.CurrentPeriodEnd, ct);
            }

            await TryRefreshUserTokensOnPaymentAsync(context.User.Id, ct);
            await userSubscriptionRepository.UpdateAsync(context.UserSubscription, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await emailService.SendSubscriptionUpdatedAsync(context.User, subscriptionChangeType, subscriptionState.Price, oldTierName, subscriptionState.CurrentPeriodEnd, ct);
            await scope.CommitAsync(ct);

            return Result.Success();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error handling subscription update for user {UserId}", context.User.Id);
            return Result.Failure(Errors.PaymentProcessingFailed);
        }
    }

    private async Task<SubscriptionStateSnapshot?> TryGetActiveSubscriptionStateAsync(string subscriptionId, CancellationToken ct)
    {
        var (status, currentPriceId, currentPeriodEnd) = await stripeGateway.GetSubscriptionDetailsAsync(subscriptionId, ct);
        if (!string.Equals(status, SubscriptionConstants.Status.Active, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(currentPriceId))
        {
            return null;
        }

        var price = await unitOfWork.PriceRepository.GetByStripeIdAsync(currentPriceId, ct);
        return price is null ? null : new SubscriptionStateSnapshot(price, currentPeriodEnd);
    }

    private static bool ShouldDeferDowngrade(SubscriptionChangeType subscriptionChangeType, DateTime? currentPeriodEnd) =>
        Equals(subscriptionChangeType, SubscriptionChangeType.TierDowngrade)
        && currentPeriodEnd.HasValue
        && currentPeriodEnd.Value > DateTime.UtcNow;

    private async Task ScheduleDeferredDowngradeAsync(SubscriptionWebhookContext context, int targetTierId, DateTime currentPeriodEnd, CancellationToken ct)
    {
        var expirationTime = DateTime.SpecifyKind(currentPeriodEnd, DateTimeKind.Utc);
        context.UserSubscription.UpdateDeferredDowngrade(expirationTime, targetTierId);
        await backgroundJobService.CancelDeferredDowngradeAsync(context.User.Id, ct);
        await backgroundJobService.ScheduleDeferredDowngradeAsync(context.User.Id, targetTierId, expirationTime, ct);
    }

    private async Task ApplyImmediateSubscriptionUpdateAsync(
        SubscriptionWebhookContext context,
        SubscriptionChangeType subscriptionChangeType,
        Price newPrice,
        DateTime? currentPeriodEnd,
        CancellationToken ct)
    {
        if (!context.User.TierId.Equals(newPrice.TierId))
        {
            context.User.UpdateTier(newPrice.TierId);
        }

        if (!currentPeriodEnd.HasValue)
        {
            return;
        }

        stateService.ApplyBillingTransition(context.User, context.UserSubscription, subscriptionChangeType, currentPeriodEnd.Value);
        if (!context.UserSubscription.PendingTierId.HasValue)
        {
            return;
        }

        await backgroundJobService.CancelDeferredDowngradeAsync(context.User.Id, ct);
        if (context.UserSubscription.SubscriptionExpirationTime != null)
        {
            await backgroundJobService.ScheduleDeferredDowngradeAsync(
                context.User.Id,
                context.UserSubscription.PendingTierId.Value,
                context.UserSubscription.SubscriptionExpirationTime.Value,
                ct);
        }
    }

    private async Task TryRefreshUserTokensOnPaymentAsync(long userId, CancellationToken ct)
    {
        try
        {
            await userMonthlyTokenCountRepository.ResetTokensForSubscriptionPaymentAsync(userId, ct);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error refreshing tokens for user {UserId} on payment", userId);
        }
    }

    private sealed record SubscriptionWebhookContext(User User, UserSubscription UserSubscription);

    private sealed record SubscriptionStateSnapshot(Price Price, DateTime? CurrentPeriodEnd);
}
