using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

/// <summary>
///     Response model containing detailed place information from Google Maps API via SerpApi.
///     Provides comprehensive data about a specific place including reviews, photos, hours, and more.
/// </summary>
public class GetMapsResponse
{
    /// <summary>
    ///     Metadata about the search request and response
    /// </summary>
    [JsonPropertyName("searchMetadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    /// <summary>
    ///     Parameters used in the search request
    /// </summary>
    [JsonPropertyName("searchParameters")]
    public MapsSearchParameters SearchParameters { get; set; } = new();

    /// <summary>
    ///     List of local search results (used when type=search)
    /// </summary>
    [JsonPropertyName("localResults")]
    public List<MapsPlaceResult>? LocalResults { get; set; }

    /// <summary>
    ///     Detailed information about a specific place (used when querying by place_id)
    /// </summary>
    [JsonPropertyName("placeResults")]
    public MapsPlaceResult? PlaceResults { get; set; }
}