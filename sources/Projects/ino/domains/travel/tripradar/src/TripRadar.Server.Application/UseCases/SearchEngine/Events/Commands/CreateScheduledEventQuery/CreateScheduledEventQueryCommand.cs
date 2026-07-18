using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;

public record CreateScheduledEventQueryCommand(
    string Username,
    string SearchQuery,
    IList<QueryColumn>? SelectedColumns,
    string? AdditionalParametersJson = null,
    DateTime? NextExecutionTime = null,
    string? Schedule = null) : IRequest<Result<Guid>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledEvent, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledEvent, 1, CountMetric.SetResult(false));
    }
}
