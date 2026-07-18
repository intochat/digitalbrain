using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IAirportRepository : IRepository<Airport>
{
    Task<Airport?> GetByCodeAsync(string code, CancellationToken cancellationToken = default, bool asNoTracking = true);
    Task<IEnumerable<Airport>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
    Task<Airport?> FindBestMatchAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Airport>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}
