using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class OverageBillingRecordRepository(TripRadarDbContext dbContext)
    : Repository<OverageBillingRecord>(dbContext), IOverageBillingRecordRepository
{
    public async Task<OverageBillingRecord> CreateAsync(OverageBillingRecord billingRecord,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.OverageBillingRecords.AddAsync(billingRecord, cancellationToken);
        return result.Entity;
    }

    public async Task<List<OverageBillingRecord>> GetByUserIdAndMonthAsync(long userId, int year, int month, CancellationToken cancellationToken = default) =>
        await dbContext.OverageBillingRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Year == year && r.Month == month)
            .Include(r => r.ServiceType)
            .Include(r => r.Currency)
            .ToListAsync(cancellationToken);

    public async Task<(decimal PricePerToken, int CurrencyId)?> GetOveragePricingAsync(int tierId,
        CancellationToken cancellationToken = default)
    {
        var pricing = await dbContext.OveragePricing
            .AsNoTracking()
            .Where(p => p.TierId == tierId && p.IsActive)
            .Select(p => new { p.PricePerToken, p.CurrencyId })
            .FirstOrDefaultAsync(cancellationToken);

        if (pricing is null)
        {
            return null;
        }

        return (pricing.PricePerToken, pricing.CurrencyId);
    }

    public async Task MarkAsBilledAsync(long userId, int year, int month, string stripeInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var billedAt = DateTime.UtcNow;

        await dbContext.OverageBillingRecords
            .Where(r => r.UserId == userId && r.Year == year && r.Month == month && !r.IsBilled)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(r => r.IsBilled, true)
                    .SetProperty(r => r.BilledAt, billedAt)
                    .SetProperty(r => r.StripeInvoiceId, stripeInvoiceId)
                    .SetProperty(r => r.ProcessingId, (string?)null)
                    .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null),
                cancellationToken);
    }

    public async Task MarkAsBilledByProcessingIdAsync(string processingId, string stripeInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var billedAt = DateTime.UtcNow;

        await dbContext.OverageBillingRecords
            .Where(r => r.ProcessingId == processingId && !r.IsBilled)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(r => r.IsBilled, true)
                    .SetProperty(r => r.BilledAt, billedAt)
                    .SetProperty(r => r.StripeInvoiceId, stripeInvoiceId)
                    .SetProperty(r => r.ProcessingId, (string?)null)
                    .SetProperty(r => r.ProcessingStartedAt, (DateTime?)null),
                cancellationToken);
    }
}
