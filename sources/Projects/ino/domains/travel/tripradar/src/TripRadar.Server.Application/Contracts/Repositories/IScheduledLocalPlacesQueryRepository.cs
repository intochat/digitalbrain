using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IScheduledLocalPlacesQueryRepository : IRepository<ScheduledLocalPlaceQuery>
{
    Task CreateAsync(ScheduledLocalPlaceQuery scheduledLocalPlaceQuery, CancellationToken cancellationToken = default);

    Task<ScheduledLocalPlaceQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId,
        CancellationToken cancellationToken = default);
}
