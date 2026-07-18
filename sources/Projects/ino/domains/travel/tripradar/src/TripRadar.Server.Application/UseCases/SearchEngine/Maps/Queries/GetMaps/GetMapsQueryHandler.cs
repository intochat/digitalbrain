using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Maps.Queries.GetMaps;

public class GetMapsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetMapsQuery, Result<GetMapsResponseDTO>>
{
    public Task<Result<GetMapsResponseDTO>> Handle(GetMapsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetMapsRequestDTO, GetMapsResponseDTO>(request.Request, ServiceType.Maps, Errors.MapsQueryDataNotFound, cancellationToken);
}
