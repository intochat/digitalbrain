namespace TripRadar.Server.Application.Contracts.Jobs;

public interface IResetTokensJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
