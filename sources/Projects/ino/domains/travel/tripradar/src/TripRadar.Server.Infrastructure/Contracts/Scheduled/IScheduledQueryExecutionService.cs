namespace TripRadar.Server.Infrastructure.Contracts.Scheduled;

public interface IScheduledQueryExecutionService
{
    Task ExecuteQueryAsync(Guid query, CancellationToken cancellationToken = default);
}
