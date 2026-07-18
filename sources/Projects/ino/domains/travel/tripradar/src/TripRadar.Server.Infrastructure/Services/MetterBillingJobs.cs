using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public class MetterBillingJobs(IMetterPaymentProcessor processor, IOptions<JobSettings> jobSettings) : IMetterBillingJobs
{
    public Task ClearStaleProcessingAsync(CancellationToken cancellationToken = default) =>
        processor.ClearStaleProcessingAsync(
            TimeSpan.FromMinutes(jobSettings.Value.MetterBillingJob.StaleProcessingMaxAgeMinutes), cancellationToken);

    public Task ProcessMonthlyOverageChargesAsync(CancellationToken cancellationToken = default) =>
        processor.ProcessMonthlyOverageChargesAsync(cancellationToken);
}
