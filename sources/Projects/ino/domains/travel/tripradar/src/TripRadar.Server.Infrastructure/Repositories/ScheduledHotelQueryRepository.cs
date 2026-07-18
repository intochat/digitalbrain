using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ScheduledHotelQueryRepository(TripRadarDbContext dbContext)
    : Repository<ScheduledHotelQuery>(dbContext), IScheduledHotelQueryRepository
{
    public async Task CreateAsync(ScheduledHotelQuery scheduledHotelQuery, CancellationToken cancellationToken = default)
    {
        await dbContext.ScheduledHotelQueries.AddAsync(scheduledHotelQuery, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduledHotelQuery?> GetByScheduledExecutionIdAsync(long scheduledExecutionId, CancellationToken cancellationToken = default) =>
        await dbContext
            .ScheduledHotelQueries
            .Include(sf => sf.ScheduledExecution)
            .Include(sf => sf.User)
            .Include(sf => sf.User.Tier)
            .FirstOrDefaultAsync(sf => sf.ScheduledExecutionId == scheduledExecutionId, cancellationToken);
}
