using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class GoogleLrLanguageRepository(TripRadarDbContext dbContext)
    : Repository<GoogleLrLanguage>(dbContext), IGoogleLrLanguageRepository
{
    public async Task<GoogleLrLanguage?> GetByLanguageCodeAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return null;

        var normalized = languageCode.Trim().ToLowerInvariant();
        return await dbContext.GoogleLrLanguages
            .FirstOrDefaultAsync(l => l.LanguageCode == normalized, cancellationToken);
    }
}

