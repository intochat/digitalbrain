using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.MapsPlaceResults.Queries.GetMapsPlaceResults;

public class GetMapsPlaceResultsQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetMapsPlaceResultsQuery, Result<GetMapsPlaceResultsResponseDTO>>
{
    public Task<Result<GetMapsPlaceResultsResponseDTO>> Handle(GetMapsPlaceResultsQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetMapsPlaceResultsRequestDTO, GetMapsPlaceResultsResponseDTO>(request.Request, ServiceType.MapsPlaceResults, Errors.MapsPlaceResultsDataNotFound, cancellationToken);
}
