using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.GoogleLightSearch.Queries.GetGoogleLightSearch;

public class GetGoogleLightSearchQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor)
    : IRequestHandler<GetGoogleLightSearchQuery, Result<GetGoogleLightSearchResponseDTO>>
{
    public Task<Result<GetGoogleLightSearchResponseDTO>> Handle(GetGoogleLightSearchQuery request, CancellationToken ct)
    {
        return serpApiQueryExecutor.ExecuteAsync<GetGoogleLightSearchRequestDTO, GetGoogleLightSearchResponseDTO>(
            request.Request,
            ServiceType.GoogleLightSearch,
            Errors.GoogleLightSearchDataNotFound,
            ct);
    }
}
