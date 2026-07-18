using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IPaymentService
{
    Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(User user, int targetTierId, int billingPeriodId, string? promoCode = null, CancellationToken cancellationToken = default);

    Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default);

    Task<Result> DowngradeSubscriptionAsync(User user, int targetLowerTierId, int billingPeriodId, CancellationToken cancellationToken = default);

    Task<Result> ProcessSubscriptionEventAsync(string subscriptionId, SubscriptionEventType eventType, CancellationToken cancellationToken = default);

    Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default);

    Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default);

    Task<Result<RefundResult>> CreateRefundAsync(User user, RefundType type, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default);
}
