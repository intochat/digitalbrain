using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YouTubeSearch.Queries.GetYouTubeSearch;

public class GetYouTubeSearchQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetYouTubeSearchQuery, Result<GetYouTubeSearchResponseDTO>>
{
    public Task<Result<GetYouTubeSearchResponseDTO>> Handle(GetYouTubeSearchQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetYouTubeSearchRequestDTO, GetYouTubeSearchResponseDTO>(request.Request, ServiceType.YouTubeSearch, Errors.YouTubeSearchDataNotFound, cancellationToken);
}
