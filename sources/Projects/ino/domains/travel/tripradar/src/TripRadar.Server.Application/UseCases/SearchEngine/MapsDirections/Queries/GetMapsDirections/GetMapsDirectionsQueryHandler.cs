using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.MapsDirections.Queries.GetMapsDirections;

public class GetMapsDirectionsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetMapsDirectionsQuery, Result<GetMapsDirectionsResponseDTO>>
{
    public Task<Result<GetMapsDirectionsResponseDTO>> Handle(GetMapsDirectionsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetMapsDirectionsRequestDTO, GetMapsDirectionsResponseDTO>(request.Request, ServiceType.MapsDirections, Errors.MapsDirectionsDataNotFound, cancellationToken);
}
