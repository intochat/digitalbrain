using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
/// Airport information for Flight Explore API.
/// </summary>
public class FlightExploreAirport
{
    /// <summary>
    /// IATA code of the airport.
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// IATA code of the airport (alternative field name used in flights response).
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Name of the airport.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Location of the airport (if destination is not the same as airport).
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }
}
