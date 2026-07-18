using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Transactions;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Policies;
using AppErrors = TripRadar.Server.Application.ApplicationErrors.Errors;
using ServiceType = TripRadar.Server.Domain.Enums.ServiceType;

namespace TripRadar.Server.Infrastructure.Services;

public class OverageBillingService(
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IUserSubscriptionRepository userSubscriptionRepository,
    IOverageBillingRecordRepository overageBillingRecordRepository,
    ILogger<OverageBillingService> logger,
    IHostEnvironment hostEnvironment) : IOverageBillingService
{
    public async Task<Result<(decimal TokensDeducted, decimal OverageTokensUsed, decimal OverageCharge, bool WarningSent)>> DeductTokensWithOverageAsync(User user, ServiceType serviceType, decimal tokensToDeduct, CancellationToken cancellationToken = default)
    {
        if (tokensToDeduct <= 0)
        {
            return Result.Failure<(decimal, decimal, decimal, bool)>(AppErrors.InvalidTokenAmount);
        }

        await using var scope = await unitOfWork.StartScopeAsync(isolationLevel: IsolationLevel.RepeatableRead, cancellationToken: cancellationToken);

        try
        {
            var eligibilityResult = await IsOverageEligibleAsync(user, cancellationToken);
            if (eligibilityResult.IsFailure || !eligibilityResult.Value)
            {
                return Result.Failure<(decimal, decimal, decimal, bool)>(AppErrors.PayAsYouGoNotEnabled);
            }

            var userTokenCount = await userMonthlyTokenCountRepository.GetOrCreateCurrentMonthForUpdateAsync(user, user.Profile.TimezoneCode, cancellationToken);
            var monthlyLimit = user.Tier.TokensPerMonthLimit;
            var deduction = userTokenCount.PlanConsumption(tokensToDeduct, monthlyLimit);
            decimal overageCharge = 0;

            if (deduction.TierTokens > 0)
            {
                userTokenCount.ConsumeTokens(deduction.TierTokens);
            }

            if (deduction.OverageTokens > 0)
            {
                var overagePricing = await overageBillingRecordRepository.GetOveragePricingAsync(user.TierId, cancellationToken);
                if (overagePricing == null)
                {
                    logger.LogError("No overage pricing found for tier {TierId}", user.TierId);
                    return Result.Failure<(decimal, decimal, decimal, bool)>(AppErrors.PayAsYouGoBillingFailed);
                }

                var (pricePerToken, currencyId) = overagePricing.Value;
                overageCharge = Math.Round(deduction.OverageTokens * pricePerToken, 2, MidpointRounding.AwayFromZero);

                var metadata = new { userId = user.Id, serviceType = serviceType.Name, tier = user.Tier.Name };
                var billingRecord = new OverageBillingRecord(user.Id, serviceType, deduction.OverageTokens, pricePerToken, currencyId, JsonSerializer.Serialize(metadata));
                await overageBillingRecordRepository.CreateAsync(billingRecord, cancellationToken);
                userTokenCount.ConsumeOverageTokens(deduction.OverageTokens);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);

            var totalTokensDeducted = deduction.TierTokens + deduction.OverageTokens;
            return Result.Success((totalTokensDeducted, deduction.OverageTokens, overageCharge, false));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing PAYG token deduction for user {UserId}", user.Id);
            return Result.Failure<(decimal, decimal, decimal, bool)>(AppErrors.PayAsYouGoBillingFailed);
        }
    }

    public async Task<Result<bool>> IsOverageEligibleAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);
            var requiresStripeSubscriptionId = hostEnvironment.IsProduction();
            var eligible = OverageEligibilityPolicy.IsEligible(user, userSubscription, requiresStripeSubscriptionId);

            if (!eligible && userSubscription is not null)
            {
                logger.LogInformation(
                    "PAYG ineligible for user {UserId}. Enabled={Enabled}, IsActive={IsActive}, HasStripeId={HasStripeId}, RequiresStripeId={RequiresStripeId}",
                    user.Id,
                    userSubscription.PayAsYouGoEnabled,
                    userSubscription.IsActive,
                    !string.IsNullOrWhiteSpace(userSubscription.StripeSubscriptionId),
                    requiresStripeSubscriptionId);
            }

            return Result.Success(eligible);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking PAYG eligibility for user {UserId}", user.Id);
            return Result.Failure<bool>(AppErrors.PayAsYouGoBillingFailed);
        }
    }
}


