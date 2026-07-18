using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.GoogleLightSearch.Queries.GetGoogleLightSearch;

public record GetGoogleLightSearchQuery(GetGoogleLightSearchRequestDTO Request, string Username, string? TripVaultName = null)
    : IRequest<Result<GetGoogleLightSearchResponseDTO>>, IMonitoringService, ITokenConsumingRequest, ITripVaultQueryRequest, ILocalizationRequest
{
    public ServiceType ServiceType => ServiceType.GoogleLightSearch;

    public object GetTripVaultPayload() => Request;

    public Localization? Localization => Request.Localization;

    public void IncrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetGoogleLightSearchRequest, 1, CountMetric.SetResult(true));

    public void DecrementCount(CountMetric countMetric) =>
        countMetric.UpdateMetric(MetricConstants.GetGoogleLightSearchRequest, 1, CountMetric.SetResult(false));
}
