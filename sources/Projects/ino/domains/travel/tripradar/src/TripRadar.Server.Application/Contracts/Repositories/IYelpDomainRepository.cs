using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IYelpDomainRepository : IRepository<YelpDomain>
{
    Task<YelpDomain?> GetByDomainNameAsync(string domainName, CancellationToken cancellationToken = default);
}
