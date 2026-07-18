using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class SubscriptionUpdatedHandler(
    IPaymentService paymentService,
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    ILogger<SubscriptionUpdatedHandler> logger)
    : StripeEventHandler<Subscription>(logger)
{
    public override string EventType => StripeConstants.WebhookEvents.Subscription.Updated;

    protected override async Task<Result> ProcessEventDataAsync(Subscription subscription, Event stripeEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subscription.Id))
        {
            return Result.Success();
        }

        if (ShouldPersistSubscription(subscription.Status))
        {
            var linkResult = await LinkSubscriptionToUserAsync(subscription, cancellationToken);
            if (linkResult.IsFailure)
            {
                Logger.LogError("Failed to link subscription {SubscriptionId} to user during update: {Error}", subscription.Id, linkResult.Error.Reason);
                return linkResult;
            }
        }

        var subscriptionEventType = StripeSubscriptionEventTypes.Resolve(stripeEvent.Type);
        return subscriptionEventType is null ? Result.Success() : await paymentService.ProcessSubscriptionEventAsync(subscription.Id, subscriptionEventType, cancellationToken);
    }

    protected override Task<Result> HandleInvalidDataAsync() => Task.FromResult(Result.Failure(Errors.StripeWebhookInvalidSubscription));

    private async Task<Result> LinkSubscriptionToUserAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(subscription.CustomerId))
            {
                Logger.LogWarning("Subscription {SubscriptionId} has no customer ID during update", subscription.Id);
                return Result.Success();
            }

            var userSubscription = await userSubscriptionRepository.GetByStripeCustomerIdAsync(subscription.CustomerId, cancellationToken);
            if (userSubscription is null)
            {
                Logger.LogWarning(
                    "No user subscription found for Stripe customer {CustomerId} during subscription update {SubscriptionId}",
                    subscription.CustomerId,
                    subscription.Id);
                return Result.Success();
            }

            if (string.Equals(userSubscription.StripeSubscriptionId, subscription.Id, StringComparison.Ordinal))
            {
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
            Logger.LogError(exception, "Database operation failed while linking updated subscription {SubscriptionId} to user", subscription.Id);
            return Result.Failure(Errors.StripeWebhookDatabaseOperationFailed);
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