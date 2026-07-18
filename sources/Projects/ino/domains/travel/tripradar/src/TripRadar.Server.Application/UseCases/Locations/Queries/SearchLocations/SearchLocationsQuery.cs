using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Locations.Queries.SearchLocations;

public sealed record SearchLocationsQuery(string Query, int Limit = 10)
    : IRequest<Result<IReadOnlyList<LocationSuggestionResponseDTO>>>;
