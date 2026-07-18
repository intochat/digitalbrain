using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services.UserLimits;

public sealed class UserTokenReservationService(
    UserLimitDecisionService decisionService,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository)
{
    public async Task<Result<TokenConsumptionTicket>> PrepareAsync(User user, string username, ServiceType serviceType, CancellationToken cancellationToken)
    {
        var decisionResult = await decisionService.ResolveAsync(user, serviceType, cancellationToken);
        if (decisionResult.IsFailure)
        {
            return Result.Failure<TokenConsumptionTicket>(decisionResult.Error);
        }

        var decision = decisionResult.Value!;
        if (!decision.IsAllowed)
        {
            return Result.Failure<TokenConsumptionTicket>(UserLimitDecisionService.BuildInsufficientTokensError(decision));
        }

        if (!Equals(decision.Type, TokenConsumptionType.Tier))
            return Result.Success(new TokenConsumptionTicket(username, serviceType, TokenConsumptionType.Overage, decision.TokenCost));

        var reserved = await userMonthlyTokenCountRepository.TryConsumeTokensAsync(user, decision.TokenCost, cancellationToken);
        if (reserved)
        {
            return Result.Success(new TokenConsumptionTicket(username, serviceType, TokenConsumptionType.Tier));
        }

        var refreshedDecisionResult = await decisionService.ResolveAsync(user, serviceType, cancellationToken);
        if (refreshedDecisionResult.IsFailure)
        {
            return Result.Failure<TokenConsumptionTicket>(refreshedDecisionResult.Error);
        }

        var refreshedDecision = refreshedDecisionResult.Value!;
        return Equals(refreshedDecision.Type, TokenConsumptionType.Overage) ? Result.Success(new TokenConsumptionTicket(username, serviceType, TokenConsumptionType.Overage, refreshedDecision.TokenCost)) : Result.Failure<TokenConsumptionTicket>(UserLimitDecisionService.BuildInsufficientTokensError(refreshedDecision));

    }
}
