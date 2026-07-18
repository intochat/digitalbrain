using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IScheduledEventQueryRepository
{
    Task CreateAsync(ScheduledEventQuery scheduledEventQuery, CancellationToken cancellationToken = default);

    Task<ScheduledEventQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId,
        CancellationToken cancellationToken = default);
}
