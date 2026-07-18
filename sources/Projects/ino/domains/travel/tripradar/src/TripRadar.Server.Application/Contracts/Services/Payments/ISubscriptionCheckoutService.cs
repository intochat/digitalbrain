using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface ISubscriptionCheckoutService
{
    Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(User user, int targetTierId, int billingPeriodId, string? promoCode = null, CancellationToken cancellationToken = default);

    Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default);
}
