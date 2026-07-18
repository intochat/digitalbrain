using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services.Emails;

/// <summary>
/// Service responsible for sending subscription-related email notifications.
/// </summary>
public class SubscriptionEmailService(
    IEmailService emailService,
    ILogger<SubscriptionEmailService> logger) : ISubscriptionEmailService
{
    /// <inheritdoc />
    public async Task SendSubscriptionCreatedAsync(
        User user,
        Price price,
        DateTime? periodEnd,
        CancellationToken ct = default)
    {
        try
        {
            if (!periodEnd.HasValue)
            {
                logger.LogWarning("Cannot send subscription created email for user {UserId} - no period end date provided", user.Id);
                return;
            }

            var sent = await emailService.SendSubscriptionCreatedAsync(
                user.Profile.Email,
                user.Profile.Username ?? user.Profile.Email,
                price.Tier.Name,
                price.Amount,
                price.BillingPeriod.Name,
                periodEnd.Value,
                user.Profile.Language?.LanguageCode,
                ct);

            if (sent)
                logger.LogInformation("Subscription created email sent successfully for user {UserId}", user.Id);
            else
                logger.LogWarning("Subscription created email was not sent for user {UserId}. Check email configuration and EmailService logs.", user.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send subscription created email for user {UserId}", user.Id);
        }
    }

    /// <inheritdoc />
    public async Task SendSubscriptionUpdatedAsync(
        User user,
        SubscriptionChangeType changeType,
        Price newPrice,
        string oldTierName,
        DateTime? periodEnd,
        CancellationToken ct = default)
    {
        try
        {
            if (!periodEnd.HasValue)
            {
                logger.LogWarning(
                    "Cannot send subscription updated email for user {UserId} - no period end date provided",
                    user.Id);
                return;
            }

            if (Equals(changeType, SubscriptionChangeType.TierUpgrade))
            {
                var sent = await emailService.SendSubscriptionUpgradedAsync(
                    user.Profile.Email,
                    user.Profile.Username ?? user.Profile.Email,
                    oldTierName,
                    newPrice.Tier.Name,
                    newPrice.Amount,
                    newPrice.BillingPeriod.Name,
                    periodEnd.Value,
                    user.Profile.Language?.LanguageCode,
                    ct);

                if (sent)
                    logger.LogInformation("Subscription upgraded email sent for user {UserId}: {OldTier} -> {NewTier}", user.Id, oldTierName, newPrice.Tier.Name);
                else
                    logger.LogWarning("Subscription upgraded email was not sent for user {UserId}: {OldTier} -> {NewTier}. Check email configuration and EmailService logs.", user.Id, oldTierName, newPrice.Tier.Name);
            }
            else if (Equals(changeType, SubscriptionChangeType.TierDowngrade))
            {
                var sent = await emailService.SendSubscriptionDowngradedAsync(
                    user.Profile.Email,
                    user.Profile.Username ?? user.Profile.Email,
                    oldTierName,
                    newPrice.Tier.Name,
                    newPrice.Amount,
                    newPrice.BillingPeriod.Name,
                    periodEnd.Value,
                    user.Profile.Language?.LanguageCode,
                    ct);

                if (sent)
                    logger.LogInformation("Subscription downgraded email sent for user {UserId}: {OldTier} -> {NewTier}", user.Id, oldTierName, newPrice.Tier.Name);
                else
                    logger.LogWarning("Subscription downgraded email was not sent for user {UserId}: {OldTier} -> {NewTier}. Check email configuration and EmailService logs.", user.Id, oldTierName, newPrice.Tier.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send subscription updated email for user {UserId}", user.Id);
        }
    }
}
