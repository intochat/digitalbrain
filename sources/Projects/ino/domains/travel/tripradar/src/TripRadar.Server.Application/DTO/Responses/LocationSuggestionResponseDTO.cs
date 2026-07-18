namespace TripRadar.Server.Application.DTO.Responses;

public sealed record LocationSuggestionResponseDTO(
    int LocationId,
    string Name,
    string CanonicalName,
    string CountryCode,
    string TargetType,
    double? GpsLatitude,
    double? GpsLongitude);
