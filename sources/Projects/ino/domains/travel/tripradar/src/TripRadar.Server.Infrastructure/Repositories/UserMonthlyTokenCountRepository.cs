using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class UserMonthlyTokenCountRepository(TripRadarDbContext dbContext) : Repository<UserMonthlyTokenCount>(dbContext), IUserMonthlyTokenCountRepository
{
    public async Task<UserMonthlyTokenCount> GetOrCreateCurrentMonthForUpdateAsync(User user, string timeZone, CancellationToken cancellationToken = default)
    {
        var (year, month, _) = DateTime.UtcNow;

        var tokenCount = await dbContext.UserMonthlyTokenCounts
            .AsTracking()
            .FirstOrDefaultAsync(c => c.UserId == user.Id && c.Year == year && c.Month == month, cancellationToken);

        if (tokenCount is not null)
            return tokenCount;

        var created = new UserMonthlyTokenCount(user, year, month, timeZone);
        await dbContext.UserMonthlyTokenCounts.AddAsync(created, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(created).State = EntityState.Detached;

            tokenCount = await dbContext.UserMonthlyTokenCounts
                .AsTracking()
                .FirstOrDefaultAsync(c => c.UserId == user.Id && c.Year == year && c.Month == month, cancellationToken);

            return tokenCount ?? throw new InvalidOperationException("Failed to create or load current month token counts.");
        }
    }

    public async Task<UserMonthlyTokenCount?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default) =>
        await dbContext.UserMonthlyTokenCounts.FirstOrDefaultAsync(
            c => c.UserId == userId && c.Year == DateTime.UtcNow.Year && c.Month == DateTime.UtcNow.Month,
            cancellationToken);

    public async Task<UserMonthlyTokenCount?> GetByUserIdReadOnlyAsync(long userId, CancellationToken cancellationToken = default) =>
        await dbContext.UserMonthlyTokenCounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.UserId == userId && c.Year == DateTime.UtcNow.Year && c.Month == DateTime.UtcNow.Month,
                cancellationToken);

    public async Task UpdateTokenCountAsync(User user, decimal tokenCost, string timeZone, CancellationToken cancellationToken = default)
    {
        var userTokenCount = await GetOrCreateCurrentMonthForUpdateAsync(user, timeZone, cancellationToken);
        userTokenCount.ConsumeTokens(tokenCost);
    }

    public async Task<bool> CanConsumeTokensAsync(long userId, decimal tokensToConsume, decimal tierLimit,
        CancellationToken cancellationToken = default)
    {
        var userTokenCount = await GetByUserIdReadOnlyAsync(userId, cancellationToken);

        if (userTokenCount is null)
        {
            return tokensToConsume <= tierLimit;
        }

        var currentDate = DateTime.UtcNow;
        if (!userTokenCount.IsCurrentMonth(currentDate))
        {
            return tokensToConsume <= tierLimit;
        }

        return userTokenCount.HasAvailableTokens(tokensToConsume, tierLimit);
    }

    public async Task CreateMonthlyTokenCountsAsync(User user, int year, int month, string timeZone, CancellationToken cancellationToken = default) =>
        await dbContext.UserMonthlyTokenCounts.AddAsync(new UserMonthlyTokenCount(user, year, month, timeZone), cancellationToken);

    public async Task ResetTokensForSubscriptionPaymentAsync(long userId, CancellationToken cancellationToken = default)
    {
        var currentDate = DateTime.UtcNow;
        var userTokenCount = await GetByUserIdAsync(userId, cancellationToken);

        if (userTokenCount is null)
        {
            var user = await dbContext.Users
                .Include(currentUser => currentUser.Profile)
                    .ThenInclude(profile => profile.TimezoneReference)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user != null)
            {
                await CreateMonthlyTokenCountsAsync(user, currentDate.Year, currentDate.Month, user.Profile.TimezoneCode, cancellationToken);
            }
        }
        else
        {
            userTokenCount.ResetTokensForSubscriptionPayment();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<long>> GetUsersWithOutdatedTokenCountsAsync(DateTime currentDate, CancellationToken cancellationToken = default) =>
        await dbContext.UserMonthlyTokenCounts
            .Where(umtc =>
                umtc.Year < currentDate.Year || (umtc.Year == currentDate.Year && umtc.Month < currentDate.Month))
            .Select(umtc => umtc.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<List<long>> GetUsersWithCurrentMonthTokenCountsAsync(int year, int month, CancellationToken cancellationToken = default) =>
        await dbContext.UserMonthlyTokenCounts
            .Where(umtc => umtc.Year == year && umtc.Month == month)
            .Select(umtc => umtc.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<HashSet<long>> GetUsersWithTokenCountsForMonthAsync(IReadOnlyCollection<long> userIds, int year, int month, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await dbContext.UserMonthlyTokenCounts
            .AsNoTracking()
            .Where(c => c.Year == year && c.Month == month && userIds.Contains(c.UserId))
            .Select(c => c.UserId)
            .ToHashSetAsync(cancellationToken);
    }

    public async Task CreateMonthlyTokenCountsAsync(
        IReadOnlyCollection<User> users,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (users.Count == 0)
        {
            return;
        }

        var tokenCounts = users
            .Select(user => new UserMonthlyTokenCount(user, year, month, user.Profile.TimezoneCode))
            .ToList();

        await dbContext.UserMonthlyTokenCounts.AddRangeAsync(tokenCounts, cancellationToken);
    }

    public async Task UpdateOverageTokenCountAsync(User user, decimal tokenCost, string timeZone,
        CancellationToken cancellationToken = default)
    {
        var currentDate = DateTime.UtcNow;
        var userTokenCount = await GetByUserIdAsync(user.Id, cancellationToken);

        if (userTokenCount is null)
        {
            await CreateMonthlyTokenCountsAsync(user, currentDate.Year, currentDate.Month, timeZone, cancellationToken);
            userTokenCount = await GetByUserIdAsync(user.Id, cancellationToken);
        }

        if (userTokenCount is null)
        {
            return;
        }

        if (!userTokenCount.IsCurrentMonth(currentDate))
        {
            userTokenCount.ResetForNewMonth(currentDate.Year, currentDate.Month);
        }

        userTokenCount.ConsumeOverageTokens(tokenCost);
    }

    public async Task<bool> TryConsumeTokensAsync(
        User user,
        decimal tokenCost,
        CancellationToken cancellationToken = default)
    {
        if (tokenCost > user.Tier.TokensPerMonthLimit) return false;

        var (year, month, _) = DateTime.UtcNow;

        var affectedRows = await dbContext.UserMonthlyTokenCounts
            .Where(c => c.UserId == user.Id
                     && c.Year == year
                     && c.Month == month
                     && c.TokensConsumed + tokenCost <= user.Tier.TokensPerMonthLimit)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.TokensConsumed, c => c.TokensConsumed + tokenCost)
                    .SetProperty(c => c.LastUpdateTime, DateTime.UtcNow),
                cancellationToken);

        if (affectedRows > 0)
            return true;

        var exists = await dbContext.UserMonthlyTokenCounts.AnyAsync(c => c.UserId == user.Id && c.Year == year && c.Month == month, cancellationToken);
        if (exists)
            return false;

        var newTokenCount = new UserMonthlyTokenCount(user, year, month, user.Profile.TimezoneCode);
        newTokenCount.ConsumeTokens(tokenCost);

        try
        {
            await dbContext.UserMonthlyTokenCounts.AddAsync(newTokenCount, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(newTokenCount).State = EntityState.Detached;

            affectedRows = await dbContext.UserMonthlyTokenCounts
                .Where(c => c.UserId == user.Id
                         && c.Year == year
                         && c.Month == month
                         && c.TokensConsumed + tokenCost <= user.Tier.TokensPerMonthLimit)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.TokensConsumed, c => c.TokensConsumed + tokenCost)
                        .SetProperty(c => c.LastUpdateTime, DateTime.UtcNow),
                    cancellationToken);

            return affectedRows > 0;
        }
    }

    public async Task<bool> TryRefundTokensAsync(
        User user,
        decimal tokenCost,
        CancellationToken cancellationToken = default)
    {
        var (year, month, _) = DateTime.UtcNow;

        var affectedRows = await dbContext.UserMonthlyTokenCounts
            .Where(c => c.UserId == user.Id
                     && c.Year == year
                     && c.Month == month
                     && c.TokensConsumed >= tokenCost)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.TokensConsumed, c => c.TokensConsumed - tokenCost)
                    .SetProperty(c => c.LastUpdateTime, DateTime.UtcNow),
                cancellationToken);

        return affectedRows > 0;
    }
}
