using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ScheduledFlightQueryRepository(TripRadarDbContext dbContext)
    : Repository<ScheduledFlightQuery>(dbContext), IScheduledFlightQueryRepository
{
    public async Task CreateAsync(ScheduledFlightQuery scheduledFlightQuery, CancellationToken cancellationToken = default)
    {
        await dbContext.ScheduledFlightQueries.AddAsync(scheduledFlightQuery, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduledFlightQuery?> GetByScheduledExecutionIdAsync(long scheduledExectionId, CancellationToken cancellationToken = default) =>
        await dbContext
            .ScheduledFlightQueries
            .Include(sf => sf.DepartureAirport)
            .Include(sf => sf.DestinationAirport)
            .Include(sf => sf.ScheduledExecution)
            .Include(sf => sf.User)
            .Include(sf => sf.User.Tier)
            .FirstOrDefaultAsync(sf => sf.ScheduledExecutionId == scheduledExectionId, cancellationToken);
}
