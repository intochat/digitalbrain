using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IPromoCodeRepository : IRepository<PromoCode>
{
    Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task UpdatePromoCodeAsync(PromoCode promoCode, CancellationToken cancellationToken = default);
}
