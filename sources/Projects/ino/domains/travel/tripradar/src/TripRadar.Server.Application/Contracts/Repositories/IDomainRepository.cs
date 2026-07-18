using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IDomainRepository : IRepository<GoogleDomain>
{
    Task<GoogleDomain?> GetByDomainNameAsync(string domainName, CancellationToken cancellationToken = default);
}
