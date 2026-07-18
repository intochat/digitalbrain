using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ITimezoneRepository : IRepository<Timezone>
{
    Task<List<Timezone>> GetAllTimezonesAsync(CancellationToken cancellationToken = default);
}

