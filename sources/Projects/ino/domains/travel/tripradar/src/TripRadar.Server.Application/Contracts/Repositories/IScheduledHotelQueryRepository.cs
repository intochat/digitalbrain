using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IScheduledHotelQueryRepository : IRepository<ScheduledHotelQuery>
{
    Task CreateAsync(ScheduledHotelQuery scheduledHotelQuery, CancellationToken cancellationToken = default);

    Task<ScheduledHotelQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId,
        CancellationToken cancellationToken = default);
}
