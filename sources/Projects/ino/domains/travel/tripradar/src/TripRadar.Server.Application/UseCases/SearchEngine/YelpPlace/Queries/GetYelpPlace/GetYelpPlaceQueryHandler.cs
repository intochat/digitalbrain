using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpPlace.Queries.GetYelpPlace;

public class GetYelpPlaceQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetYelpPlaceQuery, Result<GetYelpPlaceResponseDTO>>
{
    public Task<Result<GetYelpPlaceResponseDTO>> Handle(GetYelpPlaceQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetYelpPlaceRequestDTO, GetYelpPlaceResponseDTO>(request.Request, ServiceType.YelpPlace, Errors.YelpPlaceDataNotFound, cancellationToken);
}
