using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
/// Destination result from Google Travel Explore API.
/// </summary>
public class FlightExploreDestination
{
    /// <summary>
    /// Unique kgmid identifier for the destination.
    /// </summary>
    [JsonPropertyName("destination_id")]
    public string? DestinationId { get; set; }

    /// <summary>
    /// Name of the destination.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Country of the destination.
    /// </summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>
    /// GPS coordinates of the destination.
    /// </summary>
    [JsonPropertyName("gps_coordinates")]
    public GpsCoordinates? GpsCoordinates { get; set; }

    /// <summary>
    /// URL of the thumbnail image.
    /// </summary>
    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    /// <summary>
    /// Destination airport information.
    /// </summary>
    [JsonPropertyName("destination_airport")]
    public FlightExploreAirport? DestinationAirport { get; set; }

    /// <summary>
    /// Start date of the trip (YYYY-MM-DD).
    /// </summary>
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    /// <summary>
    /// End date of the trip (YYYY-MM-DD).
    /// </summary>
    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    /// <summary>
    /// Price of the flight.
    /// </summary>
    [JsonPropertyName("flight_price")]
    public decimal? FlightPrice { get; set; }

    /// <summary>
    /// Price of the hotel.
    /// </summary>
    [JsonPropertyName("hotel_price")]
    public decimal? HotelPrice { get; set; }

    /// <summary>
    /// Duration of the flight in minutes.
    /// </summary>
    [JsonPropertyName("flight_duration")]
    public int? FlightDuration { get; set; }

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

    /// <summary>
    /// Link to Google Travel Explore page for this destination.
    /// </summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    /// <summary>
    /// Link to SerpApi API request for this destination.
    /// </summary>
    [JsonPropertyName("serpapi_link")]
    public string? SerpApiLink { get; set; }
}
