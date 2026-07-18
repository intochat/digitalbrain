using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IOverageBillingRecordRepository : IRepository<OverageBillingRecord>
{
    Task<OverageBillingRecord> CreateAsync(OverageBillingRecord billingRecord,
        CancellationToken cancellationToken = default);

    Task<List<OverageBillingRecord>> GetByUserIdAndMonthAsync(long userId, int year, int month,
        CancellationToken cancellationToken = default);

    Task<(decimal PricePerToken, int CurrencyId)?> GetOveragePricingAsync(int tierId,
        CancellationToken cancellationToken = default);

    Task MarkAsBilledAsync(long userId, int year, int month, string stripeInvoiceId,
        CancellationToken cancellationToken = default);

    Task MarkAsBilledByProcessingIdAsync(string processingId, string stripeInvoiceId,
        CancellationToken cancellationToken = default);
}
