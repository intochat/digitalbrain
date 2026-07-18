using TripRadar.Server.Domain.Entities;
using ServiceType = TripRadar.Server.Domain.Enums.ServiceType;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IServiceTokenCostRepository : IRepository<ServiceTokenCost>
{
    Task<decimal?> GetTokenCostAsync(ServiceType serviceType, CancellationToken cancellationToken = default);
}
