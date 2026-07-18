using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IFlightQueryOrchestrator
{
    Task<Result<GetFlightResponseDTO>> ExecuteAsync(GetFlightRequestDTO request, CancellationToken cancellationToken);
}
