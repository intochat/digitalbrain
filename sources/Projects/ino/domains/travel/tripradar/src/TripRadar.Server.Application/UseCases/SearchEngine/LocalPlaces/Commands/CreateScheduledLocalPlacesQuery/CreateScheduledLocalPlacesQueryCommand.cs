using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Commands.CreateScheduledLocalPlacesQuery;

public record CreateScheduledLocalPlacesQueryCommand(
    string Username,
    string SearchQuery,
    string Location,
    int? Radius,
    string? Schedule,
    DateTime? NextExecutionTime,
    string? AdditionalParametersJson,
    IList<QueryColumn>? SelectedColumns) : IRequest<Result<Guid>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledLocalPlaces, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledLocalPlaces, 1, CountMetric.SetResult(false));
    }
}
