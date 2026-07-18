using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Locations.Queries.SearchLocations;

public sealed class SearchLocationsQueryHandler(
    IUnitOfWork unitOfWork) : IRequestHandler<SearchLocationsQuery, Result<IReadOnlyList<LocationSuggestionResponseDTO>>>
{
    public async Task<Result<IReadOnlyList<LocationSuggestionResponseDTO>>> Handle(SearchLocationsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Result.Success<IReadOnlyList<LocationSuggestionResponseDTO>>([]);
        }

        var locations = await unitOfWork.LocationRepository.SearchAsync(request.Query, request.Limit, cancellationToken);
        if (locations.Count == 0)
        {
            return Result.Success<IReadOnlyList<LocationSuggestionResponseDTO>>([]);
        }

        var suggestions = locations
            .Select(location => new LocationSuggestionResponseDTO(
                location.LocationId,
                location.Name.Trim(),
                location.CanonicalName.Trim(),
                location.CountryCode.Trim().ToUpperInvariant(),
                location.TargetType.Trim(),
                location.GpsLatitude,
                location.GpsLongitude))
            .ToList();

        return Result.Success<IReadOnlyList<LocationSuggestionResponseDTO>>(suggestions);
    }
}
