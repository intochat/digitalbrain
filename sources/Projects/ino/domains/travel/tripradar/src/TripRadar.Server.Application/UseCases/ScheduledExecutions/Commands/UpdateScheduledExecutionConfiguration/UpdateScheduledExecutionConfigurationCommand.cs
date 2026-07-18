using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;

public record UpdateScheduledExecutionConfigurationCommand(
    Guid ScheduledExecutionUniqueId,
    string Username,
    bool IsActive,
    string? Schedule = null,
    DateTime? NextExecutionTime = null) : IRequest<Result>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateScheduledFlightStatus, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateScheduledFlightStatus, 1, CountMetric.SetResult(false));
    }
}
