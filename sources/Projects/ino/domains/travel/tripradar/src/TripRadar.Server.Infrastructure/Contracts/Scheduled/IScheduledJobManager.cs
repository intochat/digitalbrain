namespace TripRadar.Server.Infrastructure.Contracts.Scheduled;

public interface IScheduledJobManager
{
    void RemoveIfExists(string jobId);
}
