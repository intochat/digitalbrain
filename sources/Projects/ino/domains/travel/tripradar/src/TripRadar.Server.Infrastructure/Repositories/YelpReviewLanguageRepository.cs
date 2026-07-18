using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class YelpReviewLanguageRepository(TripRadarDbContext dbContext)
    : Repository<YelpReviewLanguage>(dbContext), IYelpReviewLanguageRepository
{
    public async Task<YelpReviewLanguage?> GetByLanguageCodeAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var normalized = languageCode.Trim().ToLowerInvariant();
        return await dbContext.YelpReviewLanguages
            .FirstOrDefaultAsync(l => l.LanguageCode == normalized, cancellationToken);
    }
}
