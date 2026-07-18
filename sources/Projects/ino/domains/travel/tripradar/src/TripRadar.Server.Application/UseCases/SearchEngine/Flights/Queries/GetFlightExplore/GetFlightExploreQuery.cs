using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightExplore;

/// <summary>
/// Query for Google Travel Explore API (flight destination exploration).
/// Implements ITokenConsumingRequest for token charging after success.
/// </summary>
public record GetFlightExploreQuery(GetFlightExploreRequestDTO Request, string Username)
    : IRequest<Result<GetFlightExploreResponseDTO>>, IMonitoringService, ITokenConsumingRequest
{
    public ServiceType ServiceType => ServiceType.FlightExplore;

    public void IncrementCount(CountMetric countMetric) => 
        countMetric.UpdateMetric(MetricConstants.GetFlightExploreRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) => 
        countMetric.UpdateMetric(MetricConstants.GetFlightExploreRequest, 1, CountMetric.SetResult(false));
}
