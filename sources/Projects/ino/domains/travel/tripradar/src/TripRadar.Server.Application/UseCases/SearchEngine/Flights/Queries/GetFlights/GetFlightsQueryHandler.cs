using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;

public class GetFlightsQueryHandler(IFlightQueryOrchestrator flightQueryOrchestrator) : IRequestHandler<GetFlightsQuery, Result<GetFlightResponseDTO>>
{
    public Task<Result<GetFlightResponseDTO>> Handle(GetFlightsQuery request, CancellationToken cancellationToken) =>
        flightQueryOrchestrator.ExecuteAsync(request.GetFlightRequestDto, cancellationToken);
}
