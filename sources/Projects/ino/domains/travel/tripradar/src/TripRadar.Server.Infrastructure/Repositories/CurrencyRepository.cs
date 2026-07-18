using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class CurrencyRepository(TripRadarDbContext dbContext) : Repository<Currency>(dbContext), ICurrencyRepository
{
    public async Task<Currency?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await dbContext.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CurrencyCode == code.ToLowerInvariant(), cancellationToken);
}
