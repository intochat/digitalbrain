using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Jobs;

public class ResetTokensJob(
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IDistributedLockService distributedLockService,
    IOptions<JobSettings> jobSettings,
    ILogger<ResetTokensJob> logger) : IResetTokensJob
{
    private const string LockKey = "reset_tokens_job_lock";
    private const string JobName = "ResetTokensJob";
    private readonly JobSettings _jobSettings = jobSettings.Value;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("{JobName} starting", JobName);

            await using var lockHandle = await distributedLockService.TryAcquireLockAsync(
                LockKey,
                TimeSpan.FromMinutes(_jobSettings.ResetTokensJob.LockTimeoutMinutes),
                TimeSpan.Zero,
                cancellationToken);

            if (lockHandle == null)
            {
                logger.LogInformation("{JobName} skipped - another instance is running", JobName);
                return;
            }

            logger.LogInformation("{JobName} running", JobName);
            var (totalProcessed, totalAllocated, totalSkipped, totalErrors) =
                await AllocateTokensForUsersAsync(cancellationToken);

            logger.LogInformation(
                "{JobName} completed - Processed: {TotalProcessed}, Allocated: {TotalAllocated}, Skipped: {TotalSkipped}, Errors: {TotalErrors}",
                JobName,
                totalProcessed,
                totalAllocated,
                totalSkipped,
                totalErrors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{JobName} failed after {DurationMs}ms", JobName, stopwatch.ElapsedMilliseconds);
            throw new InternalErrorException("Monthly token allocation job failed", ex);
        }
    }

    private async Task<(int totalProcessed, int totalAllocated, int totalSkipped, int totalErrors)> AllocateTokensForUsersAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateTime.UtcNow;
        var totalProcessed = 0;
        var totalAllocated = 0;
        var totalSkipped = 0;
        var totalErrors = 0;
        var batchSize = _jobSettings.ResetTokensJob.BatchSize;

        var eligibleUserIds = await GetEligibleUsersForTokenAllocationAsync(currentDate, cancellationToken);

        for (var i = 0; i < eligibleUserIds.Count; i += batchSize)
        {
            var batch = eligibleUserIds.Skip(i).Take(batchSize).ToList();
            var (allocated, skipped, errors) = await ProcessUserBatchAsync(batch, currentDate, cancellationToken);

            totalProcessed += batch.Count;
            totalAllocated += allocated;
            totalSkipped += skipped;
            totalErrors += errors;

            logger.LogInformation(
                "{JobName} progress - Batch {BatchNumber}/{TotalBatches} - Processed: {TotalProcessed}/{Total}",
                JobName,
                i / batchSize + 1,
                (eligibleUserIds.Count + batchSize - 1) / batchSize,
                totalProcessed,
                eligibleUserIds.Count);
        }

        logger.LogInformation(
            "Token allocation summary: {TotalProcessed} users processed, {TotalAllocated} allocated, {TotalSkipped} skipped, {TotalErrors} errors",
            totalProcessed,
            totalAllocated,
            totalSkipped,
            totalErrors);

        return (totalProcessed, totalAllocated, totalSkipped, totalErrors);
    }

    private async Task<List<long>> GetEligibleUsersForTokenAllocationAsync(DateTime currentDate, CancellationToken cancellationToken)
    {
        var usersWithOutdatedTokens = await userMonthlyTokenCountRepository
            .GetUsersWithOutdatedTokenCountsAsync(currentDate, cancellationToken);

        var allActiveUserIds = await unitOfWork.UserRepository.GetAllActiveUserIdsAsync(cancellationToken);
        var usersWithCurrentMonthTokens = await userMonthlyTokenCountRepository
            .GetUsersWithCurrentMonthTokenCountsAsync(currentDate.Year, currentDate.Month, cancellationToken);

        var usersWithoutCurrentTokens = allActiveUserIds.Except(usersWithCurrentMonthTokens).ToList();
        var eligibleUsers = usersWithOutdatedTokens.Union(usersWithoutCurrentTokens).Distinct().ToList();

        return eligibleUsers;
    }

    private async Task<(int allocated, int skipped, int errors)> ProcessUserBatchAsync(List<long> userIds, DateTime currentDate, CancellationToken cancellationToken)
    {
        var allocated = 0;
        var skipped = 0;
        var errors = 0;

        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        try
        {
            var users = await unitOfWork.UserRepository.GetUsersByIdsWithTierAsync(userIds, cancellationToken);
            var existingTokenUsers = await userMonthlyTokenCountRepository
                .GetUsersWithTokenCountsForMonthAsync(users.Select(user => user.Id).ToList(), currentDate.Year, currentDate.Month, cancellationToken);

            var usersToAllocate = new List<User>(users.Count);

            foreach (var user in users)
            {
                try
                {
                    if (existingTokenUsers.Contains(user.Id))
                    {
                        skipped++;
                    }
                    else
                    {
                        usersToAllocate.Add(user);
                        allocated++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing user {UserId} for token allocation", user.Id);
                    errors++;
                }
            }

            await userMonthlyTokenCountRepository.CreateMonthlyTokenCountsAsync(usersToAllocate, currentDate.Year, currentDate.Month, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing user batch, rolling back transaction");
            throw;
        }

        return (allocated, skipped, errors);
    }
}
