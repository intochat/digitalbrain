using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IAirlineRepository : IRepository<Airline>
{
    Task<List<Airline>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    Task<Airline?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<List<Airline>> SearchActiveAsync(string? query, int limit, CancellationToken cancellationToken = default);

    Task<List<string>> GetInvalidCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
}
