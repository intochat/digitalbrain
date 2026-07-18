using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetByCountryCodeAsync(string countryCode, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCanonicalNameAsync(string canonicalName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Location>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}
