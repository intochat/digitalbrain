using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public sealed class PaymentService(
    ISubscriptionManagementService subscriptionManagementService,
    IRefundService refundService) : IPaymentService
{
    public Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(User user, int targetTierId, int billingPeriodId, string? promoCode = null, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.CreateSubscriptionCheckoutAsync(user, targetTierId, billingPeriodId, promoCode, cancellationToken);

    public Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.CancelSubscriptionAsync(user, cancellationToken);

    public Task<Result> DowngradeSubscriptionAsync(User user, int targetLowerTierId, int billingPeriodId, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.DowngradeSubscriptionAsync(user, targetLowerTierId, billingPeriodId, cancellationToken);

    public Task<Result> ProcessSubscriptionEventAsync(string subscriptionId, SubscriptionEventType eventType, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.ProcessSubscriptionEventAsync(subscriptionId, eventType, cancellationToken);

    public Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.CreateSetupIntentAsync(user, cancellationToken);

    public Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.ProcessDeferredDowngradeAsync(user, targetTierId, cancellationToken);

    public Task<Result<RefundResult>> CreateRefundAsync(User user, RefundType type, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) =>
        refundService.CreateRefundAsync(user, type, metadata, cancellationToken);

    public Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default) =>
        subscriptionManagementService.UpdatePayAsYouGoAsync(user, enabled, cancellationToken);
}
