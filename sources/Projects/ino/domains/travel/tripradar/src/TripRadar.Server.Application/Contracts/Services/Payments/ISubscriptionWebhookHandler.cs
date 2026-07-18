using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

/// <summary>
/// Handler for processing subscription webhook events from payment providers.
public interface ISubscriptionWebhookHandler
{
    /// <summary>
    /// Processes a subscription event from the payment provider webhook.
    /// </summary>
    /// <param name="subscriptionId">The payment provider subscription ID.</param>
    /// <param name="eventType">The type of subscription event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or failure of the event processing.</returns>
    Task<Result> ProcessEventAsync(
        string subscriptionId,
        SubscriptionEventType eventType,
        CancellationToken ct = default);
}
