using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;
using ServiceType = TripRadar.Server.Domain.Enums.ServiceType;

namespace TripRadar.Server.Infrastructure.Repositories;

public class ServiceTokenCostRepository(TripRadarDbContext dbContext)
    : Repository<ServiceTokenCost>(dbContext), IServiceTokenCostRepository
{
    public async Task<decimal?> GetTokenCostAsync(ServiceType serviceType,
        CancellationToken cancellationToken = default)
    {
        var serviceTokenCost = await dbContext.ServiceTokenCosts.FirstOrDefaultAsync(stc => stc.ServiceTypeId == serviceType.Id, cancellationToken);
        return serviceTokenCost?.Cost;
    }
}
