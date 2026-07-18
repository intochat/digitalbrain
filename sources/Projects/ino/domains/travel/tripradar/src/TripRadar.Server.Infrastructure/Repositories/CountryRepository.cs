using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class CountryRepository(TripRadarDbContext dbContext) : Repository<Country>(dbContext), ICountryRepository
{
    public async Task<Country?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await dbContext.Countries
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CountryCode == code.ToLowerInvariant(), cancellationToken);

    public async Task<IReadOnlyList<Country>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var normalizedCodes = codes.Select(c => c.ToLowerInvariant()).ToArray();
        return await dbContext.Countries
            .AsNoTracking()
            .Where(c => normalizedCodes.Contains(c.CountryCode.ToLower()))
            .ToListAsync(cancellationToken);
    }

    public async Task<int?> GetCountryIdByCodeAsync(string countryCode, CancellationToken cancellationToken = default) =>
        await dbContext.Countries
            .AsNoTracking()
            .Where(c => c.CountryCode == countryCode.ToLowerInvariant())
            .Select(c => (int?)EF.Property<int>(c, "CountryId"))
            .FirstOrDefaultAsync(cancellationToken);
}

