using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IScheduledFlightQueryRepository : IRepository<ScheduledFlightQuery>
{
    Task CreateAsync(ScheduledFlightQuery scheduledFlightQuery, CancellationToken cancellationToken = default);

    Task<ScheduledFlightQuery?> GetByScheduledExecutionIdAsync(long scheduledExectionId, CancellationToken cancellationToken = default);
}
