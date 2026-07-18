using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Domain.Policies;

public static class UserTokenConsumptionPolicy
{
    public static TokenConsumptionDecision Evaluate(User user, UserMonthlyTokenCount? monthlyTokenCount, decimal tokenCost, bool overageEligible)
    {
        var monthlyLimit = user.Tier.TokensPerMonthLimit;
        var currentTokens = monthlyTokenCount?.TokensConsumed ?? 0m;
        var hasTierTokens = monthlyTokenCount?.HasAvailableTokens(tokenCost, monthlyLimit) ?? tokenCost <= monthlyLimit;

        return hasTierTokens ? TokenConsumptionDecision.AllowTier(currentTokens, monthlyLimit, tokenCost) :
            overageEligible ? TokenConsumptionDecision.AllowOverage(currentTokens, monthlyLimit, tokenCost) :
            TokenConsumptionDecision.Deny(currentTokens, monthlyLimit, tokenCost);
    }
}

public sealed record TokenConsumptionDecision(
    bool IsAllowed,
    TokenConsumptionType? Type,
    decimal CurrentTokens,
    decimal MonthlyLimit,
    decimal TokenCost)
{
    public static TokenConsumptionDecision AllowTier(decimal currentTokens, decimal monthlyLimit, decimal tokenCost) =>
        new(true, TokenConsumptionType.Tier, currentTokens, monthlyLimit, tokenCost);

    public static TokenConsumptionDecision AllowOverage(decimal currentTokens, decimal monthlyLimit, decimal tokenCost) =>
        new(true, TokenConsumptionType.Overage, currentTokens, monthlyLimit, tokenCost);

    public static TokenConsumptionDecision Deny(decimal currentTokens, decimal monthlyLimit, decimal tokenCost) =>
        new(false, null, currentTokens, monthlyLimit, tokenCost);
}

