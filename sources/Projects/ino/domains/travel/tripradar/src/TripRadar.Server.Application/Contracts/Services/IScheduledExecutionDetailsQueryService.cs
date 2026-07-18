using TripRadar.Server.Application.Contracts.Repositories.Models;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IScheduledExecutionDetailsQueryService
{
    Task<IReadOnlyList<ScheduledExecutionDetails>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
}
