namespace TripRadar.Server.Application.Contracts.Jobs;

public interface IDeferredDowngradeJob
{
    Task ExecuteAsync(long userId, int targetTierId, CancellationToken cancellationToken = default);
}
