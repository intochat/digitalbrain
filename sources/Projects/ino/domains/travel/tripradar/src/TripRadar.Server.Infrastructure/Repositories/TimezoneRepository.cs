using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class TimezoneRepository(TripRadarDbContext dbContext) : Repository<Timezone>(dbContext), ITimezoneRepository
{
    public async Task<List<Timezone>> GetAllTimezonesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Timezones
            .AsNoTracking()
            .OrderBy(t => t.TimezoneName)
            .ThenBy(t => t.TimezoneId)
            .ToListAsync(cancellationToken);
}
