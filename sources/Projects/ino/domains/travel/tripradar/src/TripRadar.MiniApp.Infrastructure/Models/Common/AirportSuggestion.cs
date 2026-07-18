namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public sealed record AirportSuggestion(
    string Code,
    string Name,
    string City,
    string CountryCode,
    double? Latitude,
    double? Longitude,
    int? DistanceFromCenter
);