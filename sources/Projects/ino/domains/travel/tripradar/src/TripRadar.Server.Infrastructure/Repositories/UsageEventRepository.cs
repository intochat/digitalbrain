using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class UsageEventRepository(TripRadarDbContext dbContext) : Repository<UsageEvent>(dbContext), IUsageEventRepository
{
    public async Task<IReadOnlyList<UsageDailyTimelinePoint>> GetDailyTimelineAsync(long userId, DateTime fromUtcInclusive, DateTime toUtcExclusive, int? serviceTypeId, Guid? tripVaultUniqueId, int? usageEventSourceId, CancellationToken cancellationToken = default) =>
        await BuildFilteredQuery(userId, fromUtcInclusive, toUtcExclusive, serviceTypeId, tripVaultUniqueId, usageEventSourceId)
            .GroupBy(item => item.OccurredAt.Date)
            .OrderBy(group => group.Key)
            .Select(group => new UsageDailyTimelinePoint(group.Key, group.Sum(item => item.TokensConsumed), group.Count()))
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<UsageEventListItem> Items, int TotalCount)> GetPagedEventsAsync(long userId, DateTime fromUtcInclusive, DateTime toUtcExclusive, int? serviceTypeId, Guid? tripVaultUniqueId, int? usageEventSourceId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(userId, fromUtcInclusive, toUtcExclusive, serviceTypeId, tripVaultUniqueId, usageEventSourceId);

        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
            return ([], 0);

        var items = await query
            .OrderByDescending(item => item.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new UsageEventListItem(
                item.UniqueId,
                item.OccurredAt,
                item.ServiceTypeId,
                item.UsageEventSourceId,
                item.TokensConsumed,
                item.TripVault != null ? item.TripVault.UniqueId : null,
                item.TripVault != null ? item.TripVault.Name : null))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    private IQueryable<UsageEvent> BuildFilteredQuery(long userId, DateTime fromUtcInclusive, DateTime toUtcExclusive, int? serviceTypeId, Guid? tripVaultUniqueId, int? usageEventSourceId)
    {
        var query = dbContext.UsageEvents
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId &&
                item.OccurredAt >= fromUtcInclusive &&
                item.OccurredAt < toUtcExclusive);

        if (serviceTypeId.HasValue) query = query.Where(item => item.ServiceTypeId == serviceTypeId.Value);

        if (usageEventSourceId.HasValue) query = query.Where(item => item.UsageEventSourceId == usageEventSourceId.Value);

        if (tripVaultUniqueId.HasValue) query = query.Where(item => item.TripVault != null && item.TripVault.UniqueId == tripVaultUniqueId.Value);

        return query;
    }
}
