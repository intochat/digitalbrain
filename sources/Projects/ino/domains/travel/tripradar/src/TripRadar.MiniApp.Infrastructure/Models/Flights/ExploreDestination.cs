using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record ExploreDestination(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("thumbnail")] string? Thumbnail,
        [property: JsonPropertyName("flightPrice")] decimal? FlightPrice,
        [property: JsonPropertyName("hotelPrice")] decimal? HotelPrice,
        [property: JsonPropertyName("flightDuration")] int? FlightDuration,
        [property: JsonPropertyName("numberOfStops")] int? NumberOfStops,
        [property: JsonPropertyName("airline")] string? Airline,
        [property: JsonPropertyName("startDate")] string? StartDate,
        [property: JsonPropertyName("endDate")] string? EndDate,
        [property: JsonPropertyName("destinationAirport")] ExploreAirport? DestinationAirport
    );
}