namespace TripRadar.Server.Application.Contracts.Services;

public interface IBackgroundJobService
{
    Task ScheduleDeferredDowngradeAsync(long userId, int targetTierId, DateTime scheduledTime, CancellationToken cancellationToken = default);

    Task CancelDeferredDowngradeAsync(long userId, CancellationToken cancellationToken = default);

    void EnqueueTripVaultQueryHistorySave(Guid tripVaultUniqueId, int serviceTypeId, string queryParametersJson, string? resultSummary);

    void EnqueueTierTokenDeduction(string username, int serviceTypeId);

    void EnqueueOverageTokenDeduction(string username, int serviceTypeId, decimal tokenCost);

    void EnqueueSubscriptionCancellationEmail(long userId, string? cancellationReason);

    void EnqueueSubscriptionDowngradeScheduledEmail(long userId, int targetTierId);

    Task OnJobCompletedAsync(long userId, CancellationToken cancellationToken = default);
}

