using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ICountryRepository : IRepository<Country>
{
    Task<Country?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Country>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
    Task<int?> GetCountryIdByCodeAsync(string countryCode, CancellationToken cancellationToken = default);
}
