using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface ISubscriptionLifecycleService
{
    Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default);

    Task<Result> DowngradeSubscriptionAsync(User user, int targetLowerTierId, int billingPeriodId, CancellationToken cancellationToken = default);

    Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default);

    Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default);
}
