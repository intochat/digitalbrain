using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface ICurrencyRepository : IRepository<Currency>
{
    Task<Currency?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}

