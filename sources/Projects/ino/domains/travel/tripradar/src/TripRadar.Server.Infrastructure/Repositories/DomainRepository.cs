using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class DomainRepository(TripRadarDbContext dbContext) : Repository<GoogleDomain>(dbContext), IDomainRepository
{
    public async Task<GoogleDomain?> GetByDomainNameAsync(string domainName, CancellationToken cancellationToken = default) =>
        await dbContext.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DomainName == domainName.ToLowerInvariant(), cancellationToken);
}
