using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Airports.Queries.SearchAirports;

public sealed record SearchAirportsQuery(string Query, int Limit = 10, string? Hl = null)
    : IRequest<Result<IReadOnlyList<AirportSuggestionResponseDTO>>>;
