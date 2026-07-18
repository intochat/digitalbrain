using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetFlightsResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public FlightSearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("best_flights")] public List<FlightOption>? BestFlights { get; set; }

    [JsonPropertyName("other_flights")] public List<FlightOption>? OtherFlights { get; set; }

    [JsonPropertyName("price_insights")] public FlightPriceInsights? PriceInsights { get; set; }

    [JsonPropertyName("airports")] public List<AirportInfo>? Airports { get; set; }

    [JsonPropertyName("booking_options")]
    public List<FlightBookingOption>? BookingOptions { get; set; }
}
