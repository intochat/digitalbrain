using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class SubscriptionCreatedHandler(
    IPaymentService paymentService,
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    IPromoCodeUsageRepository promoCodeUsageRepository,
    ILogger<SubscriptionCreatedHandler> logger)
    : StripeEventHandler<Subscription>(logger)
{
    public override string EventType => StripeConstants.WebhookEvents.Subscription.Created;

    protected override async Task<Result> ProcessEventDataAsync(Subscription subscription, Event stripeEvent, CancellationToken cancellationToken)
    {
        try
        {
            if (!ShouldPersistSubscription(subscription.Status))
            {
                logger.LogInformation(
                    "Skipping subscription.created linking for subscription {SubscriptionId} with status {Status}",
                    subscription.Id,
                    subscription.Status);
                return Result.Success();
            }

            var linkResult = await LinkSubscriptionToUserAsync(subscription, cancellationToken);
            if (linkResult.IsFailure)
            {
                logger.LogError("Failed to link subscription {SubscriptionId} to user: {Error}", subscription.Id, linkResult.Error.Reason);
                return linkResult;
            }

            var userSubscription = await userSubscriptionRepository.GetByStripeSubscriptionIdAsync(subscription.Id, cancellationToken);
            if (userSubscription is null)
            {
                logger.LogError("Subscription {SubscriptionId} is not linked to any user, skipping tier update", subscription.Id);
                return Result.Success();
            }

            await RecordPromoCodeUsageIfPresentAsync(subscription, userSubscription.UserId, cancellationToken);

            var subscriptionEventType = StripeSubscriptionEventTypes.Resolve(stripeEvent.Type);
            if (subscriptionEventType is null)
            {
                return Result.Success();
            }

            var result = await paymentService.ProcessSubscriptionEventAsync(subscription.Id, subscriptionEventType, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("ProcessSubscriptionEventAsync failed for subscription {SubscriptionId}: {Error}", subscription.Id, result.Error.Reason);
            }

            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error in subscription.created handler for {SubscriptionId}", subscription.Id);
            return Result.Failure(Errors.StripeWebhookEventProcessingFailed);
        }
    }

    protected override Task<Result> HandleInvalidDataAsync() => Task.FromResult(Result.Failure(Errors.StripeWebhookInvalidSubscription));

    private async Task<Result> LinkSubscriptionToUserAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(subscription.CustomerId))
            {
                logger.LogError("Subscription {SubscriptionId} has no customer ID, skipping user linking", subscription.Id);
                return Result.Success();
            }

            var userSubscription = await userSubscriptionRepository.GetByStripeCustomerIdAsync(subscription.CustomerId, cancellationToken);
            if (userSubscription is null)
            {
                logger.LogError(
                    "No user subscription found for Stripe customer {CustomerId} (subscription {SubscriptionId})",
                    subscription.CustomerId,
                    subscription.Id);
                return Result.Success();
            }

            userSubscription.UpdateStripeSubscriptionId(subscription.Id);
            await userSubscriptionRepository.UpdateAsync(userSubscription, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Database operation failed while linking subscription {SubscriptionId} to user", subscription.Id);
            return Result.Failure(Errors.StripeWebhookDatabaseOperationFailed);
        }
    }

    private async Task RecordPromoCodeUsageIfPresentAsync(Subscription subscription, long userId, CancellationToken cancellationToken)
    {
        try
        {
            if (subscription.Metadata == null ||
                !subscription.Metadata.TryGetValue("promo_code", out var promoCode) ||
                !subscription.Metadata.TryGetValue("promo_code_id", out var promoCodeIdValue) ||
                !long.TryParse(promoCodeIdValue, out var promoCodeId))
            {
                return;
            }

            var discountAmount = 0m;
            if (subscription.Metadata.TryGetValue("discount_value", out var discountValue) && decimal.TryParse(discountValue, out var parsedDiscountValue))
            {
                discountAmount = parsedDiscountValue;
            }

            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

            var persistedPromoCode = await unitOfWork.PromoCodeRepository.GetByIdAsync(promoCodeId, cancellationToken);
            if (persistedPromoCode is null)
            {
                logger.LogWarning(
                    "Promo code {PromoCodeId} not found when processing subscription {SubscriptionId} for user {UserId}",
                    promoCodeId,
                    subscription.Id,
                    userId);
                return;
            }

            var hasUsedPromoCode = await promoCodeUsageRepository.HasUserUsedPromoCodeAsync(promoCodeId, userId, cancellationToken);
            if (hasUsedPromoCode)
            {
                logger.LogInformation(
                    "Promo code usage already recorded for user {UserId} and promo code {PromoCodeId}, skipping duplicate",
                    userId,
                    promoCodeId);
                return;
            }

            var promoCodeUsage = new Domain.Entities.PromoCodeUsage(promoCodeId, userId, discountAmount);
            await promoCodeUsageRepository.AddAsync(promoCodeUsage, cancellationToken);
            persistedPromoCode.IncrementUsageCount();

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Successfully recorded promo code usage: Code={PromoCode}, User={UserId}, DiscountAmount={DiscountAmount}, SubscriptionId={SubscriptionId}",
                promoCode,
                userId,
                discountAmount,
                subscription.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to record promo code usage for subscription {SubscriptionId} and user {UserId}", subscription.Id, userId);
        }
    }

    private static bool ShouldPersistSubscription(string? status) => status switch
    {
        SubscriptionConstants.Status.Active => true,
        SubscriptionConstants.Status.Trialing => true,
        SubscriptionConstants.Status.PastDue => true,
        SubscriptionConstants.Status.Unpaid => true,
        _ => false
    };
}