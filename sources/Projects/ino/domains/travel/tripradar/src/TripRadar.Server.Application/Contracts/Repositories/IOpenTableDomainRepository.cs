using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IOpenTableDomainRepository : IRepository<OpenTableDomain>
{
    Task<OpenTableDomain?> GetByDomainNameAsync(string domainName, CancellationToken cancellationToken = default);
}

