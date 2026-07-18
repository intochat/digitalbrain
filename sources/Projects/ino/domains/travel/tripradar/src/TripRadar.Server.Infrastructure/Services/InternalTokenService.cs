using Microsoft.Extensions.Logging;
using System.Transactions;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Infrastructure.Services;

public class InternalTokenService(
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IOverageBillingService overageBillingService,
    IUsageEventWriter usageEventWriter,
    ILogger<InternalTokenService> logger) : IInternalTokenService
{
    public async Task<Result<(decimal TokensDeducted, decimal RemainingTokens, decimal MonthlyLimit, bool LimitReached)>> DeductTokensAsync(
        string username,
        decimal tokensToDeduct,
        ServiceType serviceType,
        UsageEventSourceType sourceType,
        CancellationToken cancellationToken = default)
    {
        if (tokensToDeduct <= 0)
        {
            return Result.Failure<(decimal, decimal, decimal, bool)>(Errors.InvalidTokenAmount);
        }

        await using var scope = await unitOfWork.StartScopeAsync(
            isolationLevel: IsolationLevel.RepeatableRead,
            cancellationToken: cancellationToken);

        try
        {
            var user = await unitOfWork.UserRepository.GetByUsernameWithProfileAndTierAsync(username, cancellationToken);
            if (user == null)
            {
                return Result.Failure<(decimal, decimal, decimal, bool)>(Errors.UserNotFound);
            }

            if (sourceType.Id == UsageEventSourceType.Ai.Id && !PaidTierEligibilityPolicy.IsPaidTier(user))
            {
                return Result.Failure<(decimal, decimal, decimal, bool)>(Errors.AiFeatureRequiresPaidTier);
            }

            var monthlyLimit = user.Tier.TokensPerMonthLimit;
            var isOverageEligible = await overageBillingService.IsOverageEligibleAsync(user, cancellationToken);
            if (isOverageEligible is { IsSuccess: true, Value: true })
            {
                var overageResult = await overageBillingService.DeductTokensWithOverageAsync(user, serviceType, tokensToDeduct, cancellationToken);
                if (!overageResult.IsSuccess)
                {
                    return Result.Failure<(decimal, decimal, decimal, bool)>(overageResult.Error);
                }

                var (tokensDeducted, _, _, _) = overageResult.Value;
                if (tokensDeducted > 0)
                {
                    await usageEventWriter.WriteAsync(
                        user.Id,
                        serviceType,
                        tokensDeducted,
                        sourceType,
                        DateTime.UtcNow,
                        tripVaultId: null,
                        cancellationToken);
                }

                var userTokenCount = await userMonthlyTokenCountRepository.GetByUserIdReadOnlyAsync(user.Id, cancellationToken);
                var (_, overageRemainingTokens, overageLimitReached) = userTokenCount?.GetConsumptionStatus(monthlyLimit) ?? (0, monthlyLimit, false);

                await scope.CommitAsync(cancellationToken);
                return Result.Success((tokensDeducted, overageRemainingTokens, monthlyLimit, overageLimitReached));
            }

            var currentMonthTokenCount = await userMonthlyTokenCountRepository.GetOrCreateCurrentMonthForUpdateAsync(user, user.Profile.TimezoneCode, cancellationToken);
            var (tokensConsumed, remainingTokens, limitReached) = currentMonthTokenCount.TryConsume(tokensToDeduct, monthlyLimit);

            if (tokensConsumed > 0)
            {
                await usageEventWriter.WriteAsync(
                    user.Id,
                    serviceType,
                    tokensConsumed,
                    sourceType,
                    DateTime.UtcNow,
                    tripVaultId: null,
                    cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "No tokens deducted for user {Username}: Already at monthly limit ({CurrentTokens}/{MonthlyLimit})",
                    username,
                    currentMonthTokenCount.TokensConsumed,
                    monthlyLimit);
            }

            await scope.CommitAsync(cancellationToken);
            return Result.Success((tokensConsumed, remainingTokens, monthlyLimit, limitReached));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while deducting tokens for user {Username}", username);
            return Result.Failure<(decimal, decimal, decimal, bool)>(Errors.InternalServerError);
        }
    }
}


