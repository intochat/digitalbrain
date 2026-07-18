using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class TierRepository(TripRadarDbContext dbContext) : Repository<Tier>(dbContext), ITierRepository
{
    public async Task<Tier?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        await dbContext.Tiers.FirstOrDefaultAsync(tier => tier.Name == name, cancellationToken);
}
