namespace TripRadar.Server.Application.DTO.Responses;

public sealed record AirportSuggestionResponseDTO(
    string Code,
    string Name,
    string City,
    string CountryCode,
    double? Latitude,
    double? Longitude,
    int? DistanceFromCenter,
    string? SearchAliases = null);
