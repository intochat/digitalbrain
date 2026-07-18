using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Queries.GetLocalPlaces;

public class GetLocalPlacesQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetLocalPlacesQuery, Result<GetLocalPlacesResponseDTO>>
{
    public Task<Result<GetLocalPlacesResponseDTO>> Handle(GetLocalPlacesQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetLocalPlacesRequestDTO, GetLocalPlacesResponseDTO>(request.Request, ServiceType.LocalPlaces, Errors.LocalPlacesQueryDataNotFound, cancellationToken);
}
