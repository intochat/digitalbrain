using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public abstract class SubscriptionProcessingStripeEventHandler<TStripeEntity>(
    string eventType,
    Func<TStripeEntity, string?> subscriptionIdExtractor,
    IPaymentService paymentService,
    ILogger logger)
    : StripeEventHandler<TStripeEntity>(logger)
    where TStripeEntity : class, IStripeEntity
{
    public override string EventType => eventType;

    protected override async Task<Result> ProcessEventDataAsync(
        TStripeEntity eventData,
        Event stripeEvent,
        CancellationToken cancellationToken)
    {
        var subscriptionId = subscriptionIdExtractor(eventData);
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return Result.Success();
        }

        var subscriptionEventType = StripeSubscriptionEventTypes.Resolve(stripeEvent.Type);
        if (subscriptionEventType is null)
        {
            return Result.Success();
        }

        return await paymentService.ProcessSubscriptionEventAsync(subscriptionId, subscriptionEventType, cancellationToken);
    }

    protected override Task<Result> HandleInvalidDataAsync()
    {
        var error = typeof(TStripeEntity).Name.ToLowerInvariant() switch
        {
            "subscription" => Errors.StripeWebhookInvalidSubscription,
            "invoice" => Errors.StripeWebhookInvalidInvoice,
            _ => Errors.StripeWebhookEventProcessingFailed
        };

        return Task.FromResult(Result.Failure(error));
    }
}
