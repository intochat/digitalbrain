using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;

public record CreateScheduledFlightQueryCommand(
    string DepartureAirportCode,
    string DestinationAirportCode,
    string Username,
    DateTime DepartureDate,
    DateTime? ReturnDate,
    IList<QueryColumn>? SelectedColumns,
    string? AdditionalParametersJson = null,
    DateTime? NextExecutionTime = null,
    string? Schedule = null) : IRequest<Result<Guid>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledFlight, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledFlight, 1, CountMetric.SetResult(false));
    }
}
