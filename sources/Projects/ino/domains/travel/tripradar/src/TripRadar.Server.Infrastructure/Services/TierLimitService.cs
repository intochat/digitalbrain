using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Infrastructure.Services;

public class TierLimitService(
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IServiceTokenCostRepository serviceTokenCostRepository) : ITierLimitService
{
    public async Task<bool> HasAllowedTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default)
    {
        var tokenCost = await serviceTokenCostRepository.GetTokenCostAsync(serviceType, cancellationToken);
        if (!tokenCost.HasValue)
        {
            return false;
        }

        var monthlyTokenCount = await userMonthlyTokenCountRepository.GetByUserIdReadOnlyAsync(user.Id, cancellationToken);
        var decision = UserTokenConsumptionPolicy.Evaluate(user, monthlyTokenCount, tokenCost.Value, overageEligible: false);
        return Equals(decision.Type, TokenConsumptionType.Tier);
    }

    public async Task<bool> TryReserveTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default)
    {
        var tokenCost = await serviceTokenCostRepository.GetTokenCostAsync(serviceType, cancellationToken);
        return tokenCost.HasValue && await userMonthlyTokenCountRepository.TryConsumeTokensAsync(user, tokenCost.Value, cancellationToken);
    }

    public async Task AddTokensAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default)
    {
        var tokenCost = await serviceTokenCostRepository.GetTokenCostAsync(serviceType, cancellationToken);
        if (tokenCost.HasValue)
        {
            await userMonthlyTokenCountRepository.TryConsumeTokensAsync(user, tokenCost.Value, cancellationToken);
        }
    }

    public async Task<(decimal Current, decimal Limit)> GetUserTokenStatusAsync(User user, CancellationToken cancellationToken = default)
    {
        var tokenCount = await userMonthlyTokenCountRepository.GetByUserIdReadOnlyAsync(user.Id, cancellationToken);
        return (tokenCount?.TokensConsumed ?? 0, user.Tier.TokensPerMonthLimit);
    }
}
