using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class InvoicePaymentSucceededHandler(
    IPaymentService paymentService,
    IUserSubscriptionRepository userSubscriptionRepository,
    IOverageBillingRecordRepository overageBillingRecordRepository,
    ILogger<InvoicePaymentSucceededHandler> logger)
    : SubscriptionProcessingStripeEventHandler<Invoice>(
        StripeConstants.WebhookEvents.Invoice.PaymentSucceeded,
        invoice => invoice.SubscriptionId,
        paymentService,
        logger)
{
    protected override async Task<Result> ProcessEventDataAsync(Invoice invoice, Event stripeEvent, CancellationToken cancellationToken)
    {
        var result = await base.ProcessEventDataAsync(invoice, stripeEvent, cancellationToken);

        try
        {
            if (invoice.CustomerId is null)
            {
                return result;
            }

            var userSubscription = await userSubscriptionRepository.GetByStripeCustomerIdAsync(invoice.CustomerId, cancellationToken);
            if (userSubscription is null)
            {
                return result;
            }

            var metadata = invoice.Metadata;
            if (metadata == null ||
                !TryGetMetadataValue(metadata, StripeConstants.Metadata.UserId, "userId", out var userIdValue) ||
                !long.TryParse(userIdValue, out var userId) ||
                !metadata.TryGetValue(StripeConstants.Metadata.Year, out var yearValue) ||
                !int.TryParse(yearValue, out var year) ||
                !metadata.TryGetValue(StripeConstants.Metadata.Month, out var monthValue) ||
                !int.TryParse(monthValue, out var month))
            {
                return result;
            }

            if (metadata.TryGetValue(StripeConstants.Metadata.ProcessingId, out var processingId) && !string.IsNullOrWhiteSpace(processingId))
            {
                await overageBillingRecordRepository.MarkAsBilledByProcessingIdAsync(processingId, invoice.Id, cancellationToken);
            }
            else
            {
                await overageBillingRecordRepository.MarkAsBilledAsync(userId, year, month, invoice.Id, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(
                exception,
                "Failed to reconcile PAYG billing records on invoice.payment_succeeded. InvoiceId={InvoiceId}, CustomerId={CustomerId}",
                invoice.Id,
                invoice.CustomerId);
        }

        return result;
    }

    private static bool TryGetMetadataValue(IDictionary<string, string> metadata, string primaryKey, string fallbackKey, out string value)
    {
        if (metadata.TryGetValue(primaryKey, out value!))
        {
            return true;
        }

        if (metadata.TryGetValue(fallbackKey, out value!))
        {
            return true;
        }

        value = string.Empty;
        return false;
    }
}
