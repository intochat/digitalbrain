using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class FlightOption
{
    [JsonPropertyName("flights")] public List<FlightSegment>? Flights { get; set; }

    [JsonPropertyName("layovers")] public List<Layover>? Layovers { get; set; }

    [JsonPropertyName("total_duration")] public int TotalDuration { get; set; }

    [JsonPropertyName("carbon_emissions")] public CarbonEmissions? CarbonEmissions { get; set; }

    [JsonPropertyName("price")] public decimal Price { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("airline_logo")] public string? AirlineLogo { get; set; }

    [JsonPropertyName("booking_token")] public string? BookingToken { get; set; }

    [JsonPropertyName("departure_token")] public string? DepartureToken { get; set; }

    [JsonPropertyName("buy_url")] public string? BuyUrl { get; set; }
}
