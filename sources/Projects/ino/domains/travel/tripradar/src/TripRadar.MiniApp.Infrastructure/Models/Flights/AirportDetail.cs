namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record AirportDetail(
        [property: System.Text.Json.Serialization.JsonPropertyName("airport")] AirportIdentifier? AirportId,
        string? City,
        string? Country,
        string? CountryCode,
        string? Image,
        string? Thumbnail
    );
}