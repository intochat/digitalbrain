using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ITierRepository : IRepository<Tier>
{
    Task<Tier?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
