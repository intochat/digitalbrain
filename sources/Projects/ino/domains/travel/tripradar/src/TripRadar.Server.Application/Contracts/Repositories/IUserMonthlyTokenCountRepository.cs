using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUserMonthlyTokenCountRepository : IRepository<UserMonthlyTokenCount>
{
    Task<UserMonthlyTokenCount?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);

    Task<UserMonthlyTokenCount?> GetByUserIdReadOnlyAsync(long userId, CancellationToken cancellationToken = default);

    Task<UserMonthlyTokenCount> GetOrCreateCurrentMonthForUpdateAsync(User user, string timeZone, CancellationToken cancellationToken = default);

    Task UpdateTokenCountAsync(User user, decimal tokenCost, string timeZone, CancellationToken cancellationToken = default);

    Task<bool> CanConsumeTokensAsync(long userId, decimal tokensToConsume, decimal tierLimit, CancellationToken cancellationToken = default);

    Task CreateMonthlyTokenCountsAsync(User user, int year, int month, string timeZone, CancellationToken cancellationToken = default);

    Task ResetTokensForSubscriptionPaymentAsync(long userId, CancellationToken cancellationToken = default);

    Task<List<long>> GetUsersWithOutdatedTokenCountsAsync(DateTime currentDate, CancellationToken cancellationToken = default);

    Task<List<long>> GetUsersWithCurrentMonthTokenCountsAsync(int year, int month, CancellationToken cancellationToken = default);

    Task<HashSet<long>> GetUsersWithTokenCountsForMonthAsync(
        IReadOnlyCollection<long> userIds,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task CreateMonthlyTokenCountsAsync(
        IReadOnlyCollection<User> users,
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task UpdateOverageTokenCountAsync(User user, decimal tokenCost, string timeZone, CancellationToken cancellationToken = default);

    Task<bool> TryConsumeTokensAsync(User user, decimal tokenCost, CancellationToken cancellationToken = default);

    Task<bool> TryRefundTokensAsync(User user, decimal tokenCost, CancellationToken cancellationToken = default);
}
