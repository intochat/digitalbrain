using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YouTubeSearch.Queries.GetYouTubeSearch;

public record GetYouTubeSearchQuery(GetYouTubeSearchRequestDTO Request, string Username, string? TripVaultName = null)
    : IRequest<Result<GetYouTubeSearchResponseDTO>>, IMonitoringService, ITokenConsumingRequest, ITripVaultQueryRequest
{
    public ServiceType ServiceType => ServiceType.YouTubeSearch;

    public object GetTripVaultPayload() => Request;

    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetYouTubeSearchRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetYouTubeSearchRequest, 1, CountMetric.SetResult(false));
}
