using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class PromoCodeUsageRepository(TripRadarDbContext dbContext) : Repository<PromoCodeUsage>(dbContext), IPromoCodeUsageRepository
{
    public async Task<int> GetUsageCountByUserAsync(long promoCodeId, long userId, CancellationToken cancellationToken = default) =>
        await dbContext.PromoCodeUsages
            .CountAsync(pcu => pcu.PromoCodeId == promoCodeId && pcu.UserId == userId, cancellationToken);

    public async Task<List<PromoCodeUsage>> GetUsagesByPromoCodeAsync(long promoCodeId, CancellationToken cancellationToken = default) =>
        await dbContext.PromoCodeUsages
            .Where(pcu => pcu.PromoCodeId == promoCodeId)
            .OrderByDescending(pcu => pcu.UsedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasUserUsedPromoCodeAsync(long promoCodeId, long userId, CancellationToken cancellationToken = default) =>
        await dbContext.PromoCodeUsages
            .AnyAsync(pcu => pcu.PromoCodeId == promoCodeId && pcu.UserId == userId, cancellationToken);
}
