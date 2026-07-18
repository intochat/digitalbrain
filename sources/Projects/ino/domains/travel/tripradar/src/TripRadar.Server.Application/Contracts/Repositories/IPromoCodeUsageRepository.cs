using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IPromoCodeUsageRepository : IRepository<PromoCodeUsage>
{
    Task<int> GetUsageCountByUserAsync(long promoCodeId, long userId, CancellationToken cancellationToken = default);

    Task<List<PromoCodeUsage>> GetUsagesByPromoCodeAsync(long promoCodeId, CancellationToken cancellationToken = default);

    Task<bool> HasUserUsedPromoCodeAsync(long promoCodeId, long userId, CancellationToken cancellationToken = default);
}
