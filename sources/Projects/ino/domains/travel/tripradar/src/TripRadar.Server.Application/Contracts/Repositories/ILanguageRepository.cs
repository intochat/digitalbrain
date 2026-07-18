using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ILanguageRepository : IRepository<Language>
{
    Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<List<Language>> GetAllSystemLanguagesAsync(CancellationToken cancellationToken = default);
    Task<int?> GetLanguageIdByCodeAsync(string code, CancellationToken cancellationToken = default);
}

