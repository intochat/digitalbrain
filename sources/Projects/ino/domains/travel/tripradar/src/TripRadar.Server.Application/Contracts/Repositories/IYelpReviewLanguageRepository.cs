using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IYelpReviewLanguageRepository : IRepository<YelpReviewLanguage>
{
    Task<YelpReviewLanguage?> GetByLanguageCodeAsync(string languageCode, CancellationToken cancellationToken = default);
}
