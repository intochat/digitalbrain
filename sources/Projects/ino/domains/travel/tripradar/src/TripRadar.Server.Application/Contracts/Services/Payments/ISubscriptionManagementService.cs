using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

/// <summary>
/// Service responsible for subscription lifecycle management.
/// Acts as an orchestrator/facade that delegates to specialized services.
/// </summary>
public interface ISubscriptionManagementService
{
    /// <summary>
    /// Creates checkout data for a new subscription.
    /// </summary>
    Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(
        User user,
        int targetTierId,
        int billingPeriodId,
        string? promoCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active subscription.
    /// </summary>
    Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downgrades a subscription to a lower tier.
    /// </summary>
    Task<Result> DowngradeSubscriptionAsync(
        User user,
        int targetLowerTierId,
        int billingPeriodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a deferred downgrade when the current billing period ends.
    /// </summary>
    Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the Pay-As-You-Go setting for a user.
    /// </summary>
    Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a setup intent for saving payment methods.
    /// </summary>
    Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes subscription webhook events.
    /// Uses SubscriptionEventType enum instead of string for type safety.
    /// </summary>
    Task<Result> ProcessSubscriptionEventAsync(
        string subscriptionId,
        SubscriptionEventType eventType,
        CancellationToken cancellationToken = default);
}
