using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IPriceRepository
{
    ValueTask<Price?> GetByTierIdAndBillingPeriodAsync(int tierId, int billingPeriodId, CancellationToken cancellationToken = default);

    Task<Price?> GetByStripeIdAsync(string stripeId, CancellationToken cancellationToken = default);

    ValueTask<List<Price>> GetAllAsync(CancellationToken cancellationToken = default);
}
