using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Stripe;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Contracts.Handlers;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public class StripeWebhookHandler(
    IOptions<PaymentSettings> paymentSettings,
    IHostEnvironment hostEnvironment,
    IStripeEventHandlerFactory eventHandlerFactory,
    ILogger<StripeWebhookHandler> logger) : IStripeWebhookHandler
{
    private readonly StripeSettings _stripeSettings = paymentSettings.Value.Stripe;
    private readonly bool _allowUnverifiedWebhooksInDevelopment = hostEnvironment.IsDevelopment() && paymentSettings.Value.Stripe.AllowUnverifiedWebhooksInDevelopment;

    private static readonly HashSet<string> _retryableErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        Errors.StripeWebhookDatabaseOperationFailed.Code,
        Errors.StripeWebhookEventProcessingFailed.Code,
        Errors.StripeApiConnectionFailed.Code,
        Errors.ServiceUnavailable.Code,
        Errors.InternalServerError.Code
    };

    public async Task<bool> HandleWebhookAsync(string? payload, string? signature,
        CancellationToken cancellationToken = default)
    {
        if (!_allowUnverifiedWebhooksInDevelopment && string.IsNullOrWhiteSpace(_stripeSettings.WebhookSecret))
        {
            logger.LogError("Stripe webhook secret is not configured. Webhook processing is disabled.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            logger.LogError("Invalid webhook request - missing payload.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(signature) && !_allowUnverifiedWebhooksInDevelopment)
        {
            logger.LogError(
                "Invalid webhook request - missing Stripe signature header and unverified mode is disabled.");
            return false;
        }

        try
        {
            var stripeEvent = ConstructStripeEvent(payload, signature);
            return await ProcessConstructedEventAsync(stripeEvent, cancellationToken);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Webhook event could not be parsed or signature verification failed.");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing webhook");
            return false;
        }
    }

    private Event ConstructStripeEvent(string payload, string? signature)
    {
        if (!_allowUnverifiedWebhooksInDevelopment)
        {
            return EventUtility.ConstructEvent(payload, signature!, _stripeSettings.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }

        if (!string.IsNullOrWhiteSpace(signature) && !string.IsNullOrWhiteSpace(_stripeSettings.WebhookSecret))
        {
            try
            {
                return EventUtility.ConstructEvent(payload, signature, _stripeSettings.WebhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe signature verification failed in Development. Falling back to unverified webhook parsing because AllowUnverifiedWebhooksInDevelopment=true.");
            }
        }
        else
        {
            logger.LogWarning("Stripe webhook signature verification is bypassed in Development because signing secret/signature is missing and AllowUnverifiedWebhooksInDevelopment=true.");
        }

        return EventUtility.ParseEvent(payload);
    }

    private async Task<bool> ProcessConstructedEventAsync(Event stripeEvent, CancellationToken cancellationToken)
    {
        var result = await ProcessWebhookEvent(stripeEvent, cancellationToken);
        if (result.IsSuccess)
        {
            return true;
        }

        if (ShouldRetry(result))
        {
            logger.LogError("Webhook event {EventType} processing failed: {Error}. Returning failure to trigger Stripe retry.", stripeEvent.Type, result.Error.Reason);
            return false;
        }

        logger.LogWarning("Webhook event {EventType} processing failed with non-retryable error {ErrorCode}: {ErrorReason}. Acknowledging to avoid retries.", stripeEvent.Type, result.Error.Code, result.Error.Reason);
        return true;
    }

    private async Task<Result> ProcessWebhookEvent(Event stripeEvent, CancellationToken cancellationToken)
    {
        try
        {
            var handler = eventHandlerFactory.GetHandler(stripeEvent.Type);
            if (handler != null)
            {
                return await handler.HandleAsync(stripeEvent, cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process webhook event {EventType} with ID {EventId}", stripeEvent.Type,
                stripeEvent.Id);
            return Result.Failure(
                Errors.CreateStripeWebhookEventProcessingError(stripeEvent.Type, stripeEvent.Id, ex.Message));
        }
    }

    private static bool ShouldRetry(Result result) => _retryableErrorCodes.Contains(result.Error.Code);
}
