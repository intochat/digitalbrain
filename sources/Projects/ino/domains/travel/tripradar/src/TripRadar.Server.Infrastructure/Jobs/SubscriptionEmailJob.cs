using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Emails;

namespace TripRadar.Server.Infrastructure.Jobs;

public class SubscriptionEmailJob(
    IUnitOfWork unitOfWork,
    IUserSubscriptionRepository userSubscriptionRepository,
    ITierRepository tierRepository,
    IEmailService emailService,
    ILogger<SubscriptionEmailJob> logger) : ISubscriptionEmailJob
{
    public async Task SendSubscriptionCancellationEmailAsync(long userId, string? cancellationReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await unitOfWork.UserRepository.GetByIdWithProfileAsync(userId, cancellationToken);
            if (user is null)
            {
                logger.LogError("User {UserId} not found for subscription cancellation email", userId);
                return;
            }

            var sent = await emailService.SendSubscriptionCancellationAsync(
                user.Profile.Email,
                user.Profile.Username ?? user.Profile.Email,
                cancellationReason,
                user.Profile.Language?.LanguageCode,
                cancellationToken);

            if (!sent)
                logger.LogWarning("Subscription cancellation email was not sent for user {UserId}. Check email configuration and EmailService logs.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending subscription cancellation email for user {UserId}", userId);
            throw;
        }
    }

    public async Task SendSubscriptionDowngradeScheduledEmailAsync(long userId, int targetTierId, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await unitOfWork.UserRepository.GetByIdWithProfileAsync(userId, cancellationToken);
            if (user is null)
            {
                logger.LogError("User {UserId} not found for subscription downgrade scheduled email", userId);
                return;
            }

            var currentTier = await tierRepository.GetByIdAsync(user.TierId, cancellationToken);
            var targetTier = await tierRepository.GetByIdAsync(targetTierId, cancellationToken);

            if (currentTier is null || targetTier is null)
            {
                logger.LogError("Tier data missing for subscription downgrade email. User {UserId}, CurrentTierId {CurrentTierId}, TargetTierId {TargetTierId}", userId, user.TierId, targetTierId);
                return;
            }

            var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
            var effectiveDate = userSubscription?.SubscriptionExpirationTime ?? DateTime.UtcNow.AddDays(30);

            var sent = await emailService.SendSubscriptionDowngradeScheduledAsync(
                user.Profile.Email,
                user.Profile.Username ?? user.Profile.Email,
                currentTier.Name,
                targetTier.Name,
                effectiveDate,
                user.Profile.Language?.LanguageCode,
                cancellationToken);

            if (!sent)
            {
                logger.LogWarning(
                    "Subscription downgrade scheduled email was not sent for user {UserId}. Check email configuration and EmailService logs.",
                    userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending subscription downgrade scheduled email for user {UserId}", userId);
            throw;
        }
    }
}
