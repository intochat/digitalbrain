using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpSearch.Queries.GetYelpSearch;

public class GetYelpSearchQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetYelpSearchQuery, Result<GetYelpSearchResponseDTO>>
{
    public Task<Result<GetYelpSearchResponseDTO>> Handle(GetYelpSearchQuery request, CancellationToken ct) =>
        serpApiQueryExecutor.ExecuteAsync<GetYelpSearchRequestDTO, GetYelpSearchResponseDTO>(request.Request, ServiceType.YelpSearch, Errors.YelpSearchDataNotFound, ct);
}
