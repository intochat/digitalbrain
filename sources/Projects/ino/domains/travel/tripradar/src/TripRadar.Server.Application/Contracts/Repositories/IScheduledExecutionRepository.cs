using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IScheduledExecutionRepository : IRepository<ScheduledExecution>
{
    Task CreateAsync(ScheduledExecution scheduledExecution, CancellationToken cancellationToken = default);

    Task UpdateNextExecutionTimeAsync(Guid uniqueId, DateTime nextExecutionTime, CancellationToken cancellationToken = default);

    Task UpdateActiveStatusAsync(Guid uniqueId, bool isActive, CancellationToken cancellationToken = default);

    Task UpdateConfigurationAsync(Guid uniqueId, bool isActive, string schedule, DateTime nextExecutionTime, CancellationToken cancellationToken = default);

    Task<ScheduledExecution?> GetByUniqueIdAsync(Guid uniqueId, CancellationToken cancellationToken = default);

    Task DeleteByUniqueIdAsync(Guid uniqueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledExecution>> GetActiveByUserIdAsync(long userId, CancellationToken cancellationToken = default);
}
