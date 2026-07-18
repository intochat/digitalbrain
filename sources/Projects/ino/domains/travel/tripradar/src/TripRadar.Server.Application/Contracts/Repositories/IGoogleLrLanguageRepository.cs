using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IGoogleLrLanguageRepository : IRepository<GoogleLrLanguage>
{
    Task<GoogleLrLanguage?> GetByLanguageCodeAsync(string languageCode, CancellationToken cancellationToken = default);
}

