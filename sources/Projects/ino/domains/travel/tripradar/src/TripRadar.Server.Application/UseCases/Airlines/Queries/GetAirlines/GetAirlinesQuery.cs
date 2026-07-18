using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Airlines.Queries.GetAirlines;

public sealed record GetAirlinesQuery(string? Query = null, int Limit = 500) : IRequest<Result<IEnumerable<AirlineResponseDTO>>>;
