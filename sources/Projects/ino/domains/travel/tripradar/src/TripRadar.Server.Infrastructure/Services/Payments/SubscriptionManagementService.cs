using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class SubscriptionManagementService(
    ISubscriptionCheckoutService checkoutService,
    ISubscriptionLifecycleService lifecycleService,
    ISubscriptionWebhookHandler webhookHandler)
    : ISubscriptionManagementService
{
    public Task<Result<SubscriptionCheckoutDto>> CreateSubscriptionCheckoutAsync(User user, int targetTierId, int billingPeriodId, string? promoCode = null, CancellationToken cancellationToken = default) =>
        checkoutService.CreateSubscriptionCheckoutAsync(user, targetTierId, billingPeriodId, promoCode, cancellationToken);

    public Task<Result> CancelSubscriptionAsync(User user, CancellationToken cancellationToken = default) =>
        lifecycleService.CancelSubscriptionAsync(user, cancellationToken);

    public Task<Result> DowngradeSubscriptionAsync(User user, int targetLowerTierId, int billingPeriodId, CancellationToken cancellationToken = default) =>
        lifecycleService.DowngradeSubscriptionAsync(user, targetLowerTierId, billingPeriodId, cancellationToken);

    public Task<Result> ProcessDeferredDowngradeAsync(User user, int targetTierId, CancellationToken cancellationToken = default) =>
        lifecycleService.ProcessDeferredDowngradeAsync(user, targetTierId, cancellationToken);

    public Task<Result> UpdatePayAsYouGoAsync(User user, bool enabled, CancellationToken cancellationToken = default) =>
        lifecycleService.UpdatePayAsYouGoAsync(user, enabled, cancellationToken);

    public Task<Result<string>> CreateSetupIntentAsync(User user, CancellationToken cancellationToken = default) =>
        checkoutService.CreateSetupIntentAsync(user, cancellationToken);

    public Task<Result> ProcessSubscriptionEventAsync(string subscriptionId, SubscriptionEventType eventType, CancellationToken cancellationToken = default) =>
        webhookHandler.ProcessEventAsync(subscriptionId, eventType, cancellationToken);
}
