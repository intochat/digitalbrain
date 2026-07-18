using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

/// <summary>
/// Service responsible for sending subscription-related email notifications.
/// Extracted from SubscriptionManagementService to adhere to Single Responsibility Principle.
/// </summary>
public interface ISubscriptionEmailService
{
    /// <summary>
    /// Sends an email notification when a new subscription is created.
    /// </summary>
    /// <param name="user">The user who created the subscription.</param>
    /// <param name="price">The price details of the subscription.</param>
    /// <param name="periodEnd">The end date of the current billing period.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendSubscriptionCreatedAsync(
        User user,
        Price price,
        DateTime? periodEnd,
        CancellationToken ct = default);

    /// <summary>
    /// Sends an email notification when a subscription is updated.
    /// </summary>
    /// <param name="user">The user whose subscription was updated.</param>
    /// <param name="changeType">The type of subscription change.</param>
    /// <param name="newPrice">The new price details.</param>
    /// <param name="oldTierName">The name of the previous tier.</param>
    /// <param name="periodEnd">The end date of the current billing period.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendSubscriptionUpdatedAsync(
        User user,
        SubscriptionChangeType changeType,
        Price newPrice,
        string oldTierName,
        DateTime? periodEnd,
        CancellationToken ct = default);
}
