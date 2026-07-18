namespace TripRadar.Server.Application.Contracts.Jobs;

public interface ISubscriptionEmailJob
{
    Task SendSubscriptionCancellationEmailAsync(long userId, string? cancellationReason, CancellationToken cancellationToken = default);

    Task SendSubscriptionDowngradeScheduledEmailAsync(long userId, int targetTierId, CancellationToken cancellationToken = default);
}
