using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightExplore;

public class GetFlightExploreQueryHandler(ISerpApiQueryExecutor serpApiQueryExecutor) : IRequestHandler<GetFlightExploreQuery, Result<GetFlightExploreResponseDTO>>
{
    public Task<Result<GetFlightExploreResponseDTO>> Handle(GetFlightExploreQuery request, CancellationToken cancellationToken) =>
        serpApiQueryExecutor.ExecuteAsync<GetFlightExploreRequestDTO, GetFlightExploreResponseDTO>(request.Request, ServiceType.FlightExplore, Errors.FlightExploreDataNotFound, cancellationToken);
}
