namespace TripRadar.Server.Application.Contracts.Jobs;

public interface IMetterBillingJobs
{
    Task ClearStaleProcessingAsync(CancellationToken cancellationToken = default);

    Task ProcessMonthlyOverageChargesAsync(CancellationToken cancellationToken = default);
}
