using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

/// <summary>
/// Response from Google Travel Explore API (SerpApi google_travel_explore engine).
/// Mirrors SerpApi JSON structure exactly.
/// </summary>
public class GetFlightExploreResponse
{
    /// <summary>
    /// Search metadata from SerpApi.
    /// </summary>
    [JsonPropertyName("search_metadata")]
    public SearchMetadata? SearchMetadata { get; set; }

    /// <summary>
    /// Search parameters echoed back from SerpApi.
    /// </summary>
    [JsonPropertyName("search_parameters")]
    public FlightExploreSearchParameters? SearchParameters { get; set; }

    /// <summary>
    /// List of destination results (present when exploring destinations).
    /// </summary>
    [JsonPropertyName("destinations")]
    public List<FlightExploreDestination>? Destinations { get; set; }

    /// <summary>
    /// Start date of the outbound flight (YYYY-MM-DD) when searching for specific flights.
    /// </summary>
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    /// <summary>
    /// End date of the returning flight (YYYY-MM-DD) when searching for specific flights.
    /// </summary>
    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    /// <summary>
    /// List of flight results (present when searching for specific flights between airports).
    /// </summary>
    [JsonPropertyName("flights")]
    public List<FlightExploreResult>? Flights { get; set; }
}
