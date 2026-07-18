using Hangfire;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;

namespace TripRadar.Server.Infrastructure.Services;

public class HangfireScheduledJobManager : IScheduledJobManager
{
    public void RemoveIfExists(string jobId) => RecurringJob.RemoveIfExists(jobId);
}
