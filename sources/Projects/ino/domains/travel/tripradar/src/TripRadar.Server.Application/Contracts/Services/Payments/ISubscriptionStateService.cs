using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

/// <summary>
/// Service responsible for subscription state determination and transition logic.
/// Extracted from SubscriptionManagementService to adhere to Single Responsibility Principle.
/// </summary>
public interface ISubscriptionStateService
{
    /// <summary>
    /// Determines the type of subscription change based on the user's current subscription and the new price.
    /// </summary>
    /// <param name="user">The user whose subscription is changing.</param>
    /// <param name="newPrice">The new price being applied.</param>
    /// <param name="subscription">The user's current subscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The type of subscription change.</returns>
    Task<SubscriptionChangeType> DetermineChangeTypeAsync(
        User user,
        Price newPrice,
        UserSubscription subscription,
        CancellationToken ct = default);

    /// <summary>
    /// Applies billing transition logic based on the change type.
    /// Handles cases like yearly to monthly transitions where remaining time needs to be credited.
    /// </summary>
    /// <param name="user">The user whose billing is transitioning.</param>
    /// <param name="subscription">The user's subscription to update.</param>
    /// <param name="changeType">The type of subscription change.</param>
    /// <param name="newExpirationTime">The new expiration time from the payment provider.</param>
    void ApplyBillingTransition(
        User user,
        UserSubscription subscription,
        SubscriptionChangeType changeType,
        DateTime newExpirationTime);
}
