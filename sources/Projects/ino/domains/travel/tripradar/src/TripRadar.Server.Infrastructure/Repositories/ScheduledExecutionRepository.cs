using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ScheduledExecutionRepository(TripRadarDbContext dbContext) : Repository<ScheduledExecution>(dbContext), IScheduledExecutionRepository
{
    public async Task CreateAsync(ScheduledExecution scheduledExecution, CancellationToken cancellationToken = default)
    {
        await dbContext.ScheduledExecutions.AddAsync(scheduledExecution, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateNextExecutionTimeAsync(Guid uniqueId, DateTime nextExecutionTime, CancellationToken cancellationToken = default)
    {
        var scheduledExecution = await dbContext.ScheduledExecutions.FirstOrDefaultAsync(e => e.UniqueId == uniqueId, cancellationToken);
        scheduledExecution?.UpdateNextExecutionTime(nextExecutionTime);
    }

    public async Task UpdateActiveStatusAsync(Guid uniqueId, bool isActive, CancellationToken cancellationToken = default)
    {
        var scheduledExecution = await dbContext.ScheduledExecutions.FirstOrDefaultAsync(e => e.UniqueId == uniqueId, cancellationToken);
        if (scheduledExecution is null)
        {
            return;
        }

        scheduledExecution.UpdateActiveStatus(isActive);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateConfigurationAsync(Guid uniqueId, bool isActive, string schedule, DateTime nextExecutionTime, CancellationToken cancellationToken = default)
    {
        var scheduledExecution = await dbContext.ScheduledExecutions.FirstOrDefaultAsync(e => e.UniqueId == uniqueId, cancellationToken);
        scheduledExecution?.UpdateConfiguration(isActive, schedule, nextExecutionTime);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ScheduledExecution?> GetByUniqueIdAsync(Guid uniqueId, CancellationToken cancellationToken = default) =>
        dbContext
            .ScheduledExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UniqueId == uniqueId, cancellationToken);

    public async Task<IReadOnlyList<ScheduledExecution>> GetActiveByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var scheduledExecutions = await dbContext.ScheduledExecutions
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.IsActive)
            .ToListAsync(cancellationToken);

        return scheduledExecutions;
    }

    public async Task DeleteByUniqueIdAsync(Guid uniqueId, CancellationToken cancellationToken = default)
    {
        var scheduledExecution = await dbContext.ScheduledExecutions.FirstOrDefaultAsync(e => e.UniqueId == uniqueId, cancellationToken);
        if (scheduledExecution is null)
        {
            return;
        }

        await DeleteScheduledQueriesByExecutionIdAsync(scheduledExecution.Id, cancellationToken);
        dbContext.ScheduledExecutions.Remove(scheduledExecution);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteScheduledQueriesByExecutionIdAsync(long scheduledExecutionId, CancellationToken cancellationToken)
    {
        await dbContext.ScheduledFlightQueries
            .Where(q => q.ScheduledExecutionId == scheduledExecutionId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ScheduledHotelQueries
            .Where(q => q.ScheduledExecutionId == scheduledExecutionId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ScheduledEventQueries
            .Where(q => q.ScheduledExecutionId == scheduledExecutionId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ScheduledLocalPlacesQueries
            .Where(q => q.ScheduledExecutionId == scheduledExecutionId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
