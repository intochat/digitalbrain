using Hangfire;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Infrastructure.Services;

/// <summary>
/// Background job service that stores job tracking data in the database for resilience across server restarts.
/// </summary>
public class BackgroundJobService(
    IBackgroundJobClient backgroundJobClient,
    IUserSubscriptionRepository userSubscriptionRepository,
    ILogger<BackgroundJobService> logger) : IBackgroundJobService
{
    public async Task ScheduleDeferredDowngradeAsync(long userId, int targetTierId, DateTime scheduledTime, CancellationToken cancellationToken = default)
    {
        // Cancel any existing scheduled downgrade first
        await CancelDeferredDowngradeAsync(userId, cancellationToken);

        var jobId = backgroundJobClient.Schedule<IDeferredDowngradeJob>(
            job => job.ExecuteAsync(userId, targetTierId, CancellationToken.None), scheduledTime);

        // Store the job ID in the database for resilience
        await userSubscriptionRepository.UpdateDeferredDowngradeJobIdAsync(userId, jobId, cancellationToken);

        logger.LogInformation("Scheduled deferred downgrade job {JobId} for user {UserId} at {ScheduledTime}", jobId, userId, scheduledTime);
    }

    public async Task CancelDeferredDowngradeAsync(long userId, CancellationToken cancellationToken = default)
    {
        var existingJobId = await userSubscriptionRepository.GetDeferredDowngradeJobIdAsync(userId, cancellationToken);

        if (string.IsNullOrEmpty(existingJobId))
            return;

        backgroundJobClient.Delete(existingJobId);
        await userSubscriptionRepository.UpdateDeferredDowngradeJobIdAsync(userId, null, cancellationToken);

        logger.LogInformation("Cancelled deferred downgrade job {JobId} for user {UserId}", existingJobId, userId);
    }

    public void EnqueueTripVaultQueryHistorySave(Guid tripVaultUniqueId, int serviceTypeId, string queryParametersJson, string? resultSummary) =>
        backgroundJobClient.Enqueue<ITripVaultQueryHistoryJob>(job =>
            job.SaveAsync(tripVaultUniqueId, serviceTypeId, queryParametersJson, resultSummary, CancellationToken.None));

    public void EnqueueTierTokenDeduction(string username, int serviceTypeId) =>
        backgroundJobClient.Enqueue<ITokenDeductionJob>(job => job.DeductTierTokensAsync(username, serviceTypeId, CancellationToken.None));

    public void EnqueueOverageTokenDeduction(string username, int serviceTypeId, decimal tokenCost) =>
        backgroundJobClient.Enqueue<ITokenDeductionJob>(job => job.DeductOverageTokensAsync(username, serviceTypeId, tokenCost, CancellationToken.None));

    public void EnqueueSubscriptionCancellationEmail(long userId, string? cancellationReason) =>
        backgroundJobClient.Enqueue<ISubscriptionEmailJob>(job => job.SendSubscriptionCancellationEmailAsync(userId, cancellationReason, CancellationToken.None));

    public void EnqueueSubscriptionDowngradeScheduledEmail(long userId, int targetTierId) =>
        backgroundJobClient.Enqueue<ISubscriptionEmailJob>(job => job.SendSubscriptionDowngradeScheduledEmailAsync(userId, targetTierId, CancellationToken.None));

    public async Task OnJobCompletedAsync(long userId, CancellationToken cancellationToken = default)
    {
        await userSubscriptionRepository.UpdateDeferredDowngradeJobIdAsync(userId, null, cancellationToken);
        logger.LogInformation("Cleaned up deferred downgrade job tracking for user {UserId}", userId);
    }
}

