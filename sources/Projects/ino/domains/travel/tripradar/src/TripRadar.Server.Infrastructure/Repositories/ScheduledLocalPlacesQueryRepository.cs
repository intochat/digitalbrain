using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ScheduledLocalPlacesQueryRepository(TripRadarDbContext dbContext)
    : Repository<ScheduledLocalPlaceQuery>(dbContext), IScheduledLocalPlacesQueryRepository
{
    public async Task CreateAsync(ScheduledLocalPlaceQuery scheduledLocalPlaceQuery,
        CancellationToken cancellationToken = default)
    {
        await dbContext.ScheduledLocalPlacesQueries.AddAsync(scheduledLocalPlaceQuery, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduledLocalPlaceQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId,
        CancellationToken cancellationToken = default) =>
        await dbContext
            .ScheduledLocalPlacesQueries
            .Include(sf => sf.ScheduledExecution)
            .Include(sf => sf.User)
            .Include(sf => sf.User.Tier)
            .FirstOrDefaultAsync(sf => sf.ScheduledExecutionId == scheduledExecutionId, cancellationToken);
}
