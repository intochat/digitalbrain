using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;

public record GetFlightPriceCalendarQuery(GetFlightPriceCalendarRequestDTO Request, string Username)
    : IRequest<Result<GetFlightPriceCalendarResponseDTO>>, IMonitoringService, ITokenConsumingRequest
{
    public ServiceType ServiceType => ServiceType.FlightPriceCalendar;

    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetFlightPriceCalendarRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetFlightPriceCalendarRequest, 1, CountMetric.SetResult(false));
}
