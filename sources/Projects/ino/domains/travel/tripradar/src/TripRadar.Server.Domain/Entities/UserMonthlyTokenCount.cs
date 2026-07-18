using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class UserMonthlyTokenCount : Entity<long>
{
    public UserMonthlyTokenCount()
    {
    }

    public UserMonthlyTokenCount(User user, int year, int month, string timeZone)
    {
        UserId = user.Id;
        Year = year;
        Month = month;
        TimeZone = timeZone;
        TokensConsumed = 0;
        OverageTokensConsumed = 0;
        CreatedAt = DateTime.UtcNow;
        LastUpdateTime = DateTime.UtcNow;
    }

    public new long Id { get; private set; }

    public long UserId { get; private set; }

    private User User { get; set; } = null!;

    public decimal TokensConsumed { get; private set; }

    public decimal OverageTokensConsumed { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public string TimeZone { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime LastUpdateTime { get; private set; } = DateTime.UtcNow;

    public void ConsumeTokens(decimal tokens)
    {
        TokensConsumed += tokens;
        LastUpdateTime = DateTime.UtcNow;
    }

    public void ConsumeOverageTokens(decimal tokens)
    {
        OverageTokensConsumed += tokens;
        LastUpdateTime = DateTime.UtcNow;
    }

    public void ResetForNewMonth(int newYear, int newMonth)
    {
        TokensConsumed = 0;
        OverageTokensConsumed = 0;
        Year = newYear;
        Month = newMonth;
        LastUpdateTime = DateTime.UtcNow;
    }

    public void ResetTokensForSubscriptionPayment()
    {
        TokensConsumed = 0;
        OverageTokensConsumed = 0;
        LastUpdateTime = DateTime.UtcNow;
    }

    public bool IsCurrentMonth(DateTime currentDate) => currentDate.Year == Year && currentDate.Month == Month;

    public bool HasAvailableTokens(decimal tokensNeeded, decimal monthlyQuota) => Math.Max(0, monthlyQuota - TokensConsumed) >= tokensNeeded;

    public (decimal TierTokens, decimal OverageTokens, decimal RemainingTokens, bool LimitReached) PlanConsumption(decimal tokensToDeduct, decimal monthlyLimit)
    {
        var availableTokens = Math.Max(0, monthlyLimit - TokensConsumed);
        var tierTokens = Math.Min(tokensToDeduct, availableTokens);
        var overageTokens = Math.Max(0, tokensToDeduct - tierTokens);
        var remainingTokens = Math.Max(0, availableTokens - tierTokens);
        var limitReached = TokensConsumed + tierTokens >= monthlyLimit;

        return (tierTokens, overageTokens, remainingTokens, limitReached);
    }

    public (decimal TokensConsumed, decimal RemainingTokens, bool LimitReached) TryConsume(decimal tokensToDeduct, decimal monthlyLimit)
    {
        var (tierTokens, _, remainingTokens, limitReached) = PlanConsumption(tokensToDeduct, monthlyLimit);

        if (tierTokens > 0)
        {
            ConsumeTokens(tierTokens);
        }

        return (tierTokens, remainingTokens, limitReached);
    }

    public (decimal Consumed, decimal Remaining, bool IsAtLimit) GetConsumptionStatus(decimal monthlyLimit)
    {
        var remaining = Math.Max(0, monthlyLimit - TokensConsumed);
        var isAtLimit = TokensConsumed >= monthlyLimit;
        return (TokensConsumed, remaining, isAtLimit);
    }
}
