using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;

public sealed record UpdateScheduledExecutionQueryCommand(
    Guid ScheduledExecutionUniqueId,
    string Username,
    string? SearchQuery = null,
    string? Location = null,
    int? Radius = null,
    string? DepartureAirportCode = null,
    string? DestinationAirportCode = null,
    DateTime? DepartureDate = null,
    DateTime? ReturnDate = null,
    DateTime? CheckInDate = null,
    DateTime? CheckOutDate = null,
    IList<QueryColumn>? SelectedColumns = null,
    string? AdditionalParametersJson = null) : IRequest<Result>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.UpdateScheduledExecutionQuery, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.UpdateScheduledExecutionQuery, 1, CountMetric.SetResult(false));
}
