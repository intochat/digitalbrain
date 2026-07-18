using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class LanguageRepository(TripRadarDbContext dbContext) : Repository<Language>(dbContext), ILanguageRepository
{
    public async Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await dbContext.Languages
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LanguageCode == code.ToLowerInvariant(), cancellationToken);

    public async Task<List<Language>> GetAllSystemLanguagesAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Languages
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<int?> GetLanguageIdByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await dbContext.Languages
            .AsNoTracking()
            .Where(l => l.LanguageCode == code.ToLowerInvariant())
            .Select(l => (int?)EF.Property<int>(l, "LanguageId"))
            .FirstOrDefaultAsync(cancellationToken);
}

