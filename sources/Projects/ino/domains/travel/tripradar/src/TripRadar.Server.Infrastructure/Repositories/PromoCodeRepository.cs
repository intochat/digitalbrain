using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class PromoCodeRepository(TripRadarDbContext dbContext) : Repository<PromoCode>(dbContext), IPromoCodeRepository
{
    public async Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await dbContext.PromoCodes
            .FirstOrDefaultAsync(pc => pc.Code == code && !pc.IsDeleted, cancellationToken);

    public Task UpdatePromoCodeAsync(PromoCode promoCode, CancellationToken cancellationToken = default)
    {
        dbContext.PromoCodes.Update(promoCode);
        return Task.CompletedTask;
    }
}
