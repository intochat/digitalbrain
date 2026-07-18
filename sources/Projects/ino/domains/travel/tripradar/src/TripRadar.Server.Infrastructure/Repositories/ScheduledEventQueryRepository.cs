using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ScheduledEventQueryRepository(TripRadarDbContext dbContext) : IScheduledEventQueryRepository
{
    public async Task CreateAsync(ScheduledEventQuery scheduledEventQuery,
        CancellationToken cancellationToken = default)
    {
        await dbContext.ScheduledEventQueries.AddAsync(scheduledEventQuery, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduledEventQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId,
        CancellationToken cancellationToken = default) =>
        await dbContext
            .ScheduledEventQueries
            .Include(sf => sf.ScheduledExecution)
            .Include(sf => sf.User)
            .Include(sf => sf.User.Tier)
            .FirstOrDefaultAsync(sf => sf.ScheduledExecutionId == scheduledExecutionId, cancellationToken);
}
