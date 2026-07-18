using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Policies;
using AppErrors = TripRadar.Server.Application.ApplicationErrors.Errors;

namespace TripRadar.Server.Infrastructure.Services.UserLimits;

public sealed class UserLimitDecisionService(
    IServiceTokenCostRepository serviceTokenCostRepository,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IOverageBillingService overageBillingService)
{
    public async Task<Result<TokenConsumptionDecision>> ResolveAsync(User user, ServiceType serviceType, CancellationToken cancellationToken)
    {
        var tokenCostResult = await GetTokenCostAsync(serviceType, cancellationToken);
        if (tokenCostResult.IsFailure)
        {
            return Result.Failure<TokenConsumptionDecision>(tokenCostResult.Error);
        }

        var monthlyTokenCount = await userMonthlyTokenCountRepository.GetByUserIdReadOnlyAsync(user.Id, cancellationToken);
        var overageEligibleResult = await overageBillingService.IsOverageEligibleAsync(user, cancellationToken);
        return overageEligibleResult.IsFailure
            ? Result.Failure<TokenConsumptionDecision>(overageEligibleResult.Error)
            : Result.Success(UserTokenConsumptionPolicy.Evaluate(user, monthlyTokenCount, tokenCostResult.Value, overageEligibleResult.Value));
    }

    public static Error BuildInsufficientTokensError(TokenConsumptionDecision decision) =>
        AppErrors.InsufficientTokens with
        {
            Reason = $"Your current tokens consumed: {decision.CurrentTokens}. Your token limit: {decision.MonthlyLimit}. Enable 'Metered events' to continue beyond your subscription limit."
        };

    public async Task<Result<decimal>> GetTokenCostAsync(ServiceType serviceType, CancellationToken cancellationToken)
    {
        var tokenCost = await serviceTokenCostRepository.GetTokenCostAsync(serviceType, cancellationToken);
        if (!tokenCost.HasValue)
        {
            return Result.Failure<decimal>(AppErrors.InternalServerError with
            {
                Reason = $"Token cost is not configured for service type '{serviceType.Name}'."
            });
        }

        return Result.Success(tokenCost.Value);
    }
}
