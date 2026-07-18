using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
/// Flight result from Google Travel Explore API (when searching for specific flights).
/// </summary>
public class FlightExploreResult
{
    /// <summary>
    /// Departure airport information.
    /// </summary>
    [JsonPropertyName("departure_airport")]
    public FlightExploreAirport? DepartureAirport { get; set; }

    /// <summary>
    /// Arrival airport information.
    /// </summary>
    [JsonPropertyName("arrival_airport")]
    public FlightExploreAirport? ArrivalAirport { get; set; }

    /// <summary>
    /// Duration of the flight in minutes.
    /// </summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    /// <summary>
    /// Price of the flight.
    /// </summary>
    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    /// <summary>
    /// Indicates if this is the cheapest flight.
    /// </summary>
    [JsonPropertyName("cheapest_flight")]
    public bool? CheapestFlight { get; set; }

    /// <summary>
    /// Number of stops in the flight.
    /// </summary>
    [JsonPropertyName("number_of_stops")]
    public int? NumberOfStops { get; set; }

    /// <summary>
    /// Name of the airline operating the flight.
    /// </summary>
    [JsonPropertyName("airline")]
    public string? Airline { get; set; }

    /// <summary>
    /// IATA code of the airline operating the flight.
    /// </summary>
    [JsonPropertyName("airline_code")]
    public string? AirlineCode { get; set; }
}
