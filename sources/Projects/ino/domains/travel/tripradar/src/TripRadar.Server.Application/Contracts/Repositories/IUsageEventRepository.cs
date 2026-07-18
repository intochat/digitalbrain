using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUsageEventRepository : IRepository<UsageEvent>
{
    Task<IReadOnlyList<UsageDailyTimelinePoint>> GetDailyTimelineAsync(long userId, DateTime fromUtcInclusive, DateTime toUtcExclusive, int? serviceTypeId, Guid? tripVaultUniqueId, int? usageEventSourceId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<UsageEventListItem> Items, int TotalCount)> GetPagedEventsAsync(long userId, DateTime fromUtcInclusive, DateTime toUtcExclusive, int? serviceTypeId, Guid? tripVaultUniqueId, int? usageEventSourceId, int page, int pageSize, CancellationToken cancellationToken = default);
}
