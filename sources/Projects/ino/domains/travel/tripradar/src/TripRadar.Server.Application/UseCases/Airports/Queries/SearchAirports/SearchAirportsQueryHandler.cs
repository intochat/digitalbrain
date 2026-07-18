using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.Airports.Queries.SearchAirports;

public sealed class SearchAirportsQueryHandler(IUnitOfWork unitOfWork, ICityTranslationProvider cityTranslation)
    : IRequestHandler<SearchAirportsQuery, Result<IReadOnlyList<AirportSuggestionResponseDTO>>>
{
    public async Task<Result<IReadOnlyList<AirportSuggestionResponseDTO>>> Handle(SearchAirportsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Result.Success<IReadOnlyList<AirportSuggestionResponseDTO>>([]);

        var airports = await unitOfWork.AirportRepository.SearchAsync(request.Query, request.Limit, cancellationToken);

        if (airports.Count == 0 && !string.IsNullOrWhiteSpace(request.Hl))
        {
            var englishName = cityTranslation.GetEnglishCityName(request.Query);
            if (englishName is not null)
                airports = await unitOfWork.AirportRepository.SearchAsync(englishName, request.Limit, cancellationToken);
        }

        if (airports.Count == 0)
            return Result.Success<IReadOnlyList<AirportSuggestionResponseDTO>>([]);

        return Result.Success<IReadOnlyList<AirportSuggestionResponseDTO>>(MapToSuggestions(airports));
    }

    private static List<AirportSuggestionResponseDTO> MapToSuggestions(IReadOnlyList<Airport> airports) =>
        airports
            .Select(airport => new AirportSuggestionResponseDTO(
                airport.Code.ToUpperInvariant(),
                airport.Name,
                airport.City,
                airport.Country.ToUpperInvariant(),
                airport.Latitude,
                airport.Longitude,
                DistanceFromCenter: null,
                airport.SearchAliases))
            .ToList();
}
