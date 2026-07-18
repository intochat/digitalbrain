using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ITripAdvisorDomainRepository : IRepository<TripAdvisorDomain>
{
    Task<TripAdvisorDomain?> GetByDomainNameAsync(string domainName, CancellationToken cancellationToken = default);
}
