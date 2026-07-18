using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.MapsPlaceResults.Queries.GetMapsPlaceResults;

public record GetMapsPlaceResultsQuery(GetMapsPlaceResultsRequestDTO Request, string Username, string? TripVaultName = null)
    : IRequest<Result<GetMapsPlaceResultsResponseDTO>>, IMonitoringService, ITokenConsumingRequest, ITripVaultQueryRequest
{
    public ServiceType ServiceType => ServiceType.MapsPlaceResults;

    public object GetTripVaultPayload() => Request;

    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetMapsPlaceResultsRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetMapsPlaceResultsRequest, 1, CountMetric.SetResult(false));
}
