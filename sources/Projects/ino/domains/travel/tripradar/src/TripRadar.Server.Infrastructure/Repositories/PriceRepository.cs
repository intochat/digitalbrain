using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class PriceRepository(TripRadarDbContext dbContext, IBlindIndexService blindIndexService) : IPriceRepository
{
    private static readonly Func<TripRadarDbContext, int, int, IAsyncEnumerable<Price>> _getByTierAndBillingPeriodQuery =
        EF.CompileAsyncQuery(
            (TripRadarDbContext context, int tierId, int billingPeriodId) =>
                context.Prices
                    .AsNoTracking()
                    .Where(price => price.TierId == tierId && price.BillingPeriodId == billingPeriodId)
                    .Include(price => price.Currency)
                    .Include(price => price.Tier)
                    .Include(price => price.BillingPeriod));

    private static readonly Func<TripRadarDbContext, string, IAsyncEnumerable<Price>> _getByStripeIdHashQuery =
        EF.CompileAsyncQuery(
            (TripRadarDbContext context, string stripeIdHash) =>
                context.Prices
                    .AsNoTracking()
                    .Where(price => price.StripeIdHash == stripeIdHash)
                    .Include(price => price.Currency)
                    .Include(price => price.Tier)
                    .Include(price => price.BillingPeriod));

    private static readonly Func<TripRadarDbContext, IAsyncEnumerable<Price>> _getAllPricesQuery =
        EF.CompileAsyncQuery(
            (TripRadarDbContext context) =>
                context.Prices
                    .AsNoTracking()
                    .Include(price => price.Currency)
                    .Include(price => price.Tier)
                    .Include(price => price.BillingPeriod));

    public ValueTask<Price?> GetByTierIdAndBillingPeriodAsync(int tierId, int billingPeriodId, CancellationToken cancellationToken = default) =>
        _getByTierAndBillingPeriodQuery(dbContext, tierId, billingPeriodId).FirstOrDefaultAsync(cancellationToken);

    public async Task<Price?> GetByStripeIdAsync(string stripeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stripeId))
        {
            return null;
        }

        var stripeIdHash = blindIndexService.ComputeHash(stripeId);
        if (!string.IsNullOrWhiteSpace(stripeIdHash))
        {
            var indexedMatch = await _getByStripeIdHashQuery(dbContext, stripeIdHash).FirstOrDefaultAsync(cancellationToken);
            if (indexedMatch is not null)
            {
                return indexedMatch;
            }
        }

        var prices = await _getAllPricesQuery(dbContext).ToListAsync(cancellationToken);

        return prices.FirstOrDefault(price =>
            !string.IsNullOrWhiteSpace(price.StripeId) &&
            string.Equals(price.StripeId, stripeId, StringComparison.Ordinal));
    }

    public ValueTask<List<Price>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _getAllPricesQuery(dbContext).ToListAsync(cancellationToken);
}
