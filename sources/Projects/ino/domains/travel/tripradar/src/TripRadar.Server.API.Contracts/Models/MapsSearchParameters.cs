using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
///     Search parameters used for the Maps API request
/// </summary>
public class MapsSearchParameters
{
    /// <summary>
    ///     API engine used (google_maps)
    /// </summary>
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    /// <summary>
    ///     Search type (place)
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    ///     Google Place ID used for the search
    /// </summary>
    [JsonPropertyName("placeId")]
    public string? PlaceId { get; set; }

    /// <summary>
    ///     Alternative data parameter used
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    ///     Google domain used for the search
    /// </summary>
    [JsonPropertyName("googleDomain")]
    public string GoogleDomain { get; set; } = string.Empty;

    /// <summary>
    ///     Language code used
    /// </summary>
    [JsonPropertyName("hl")]
    public string Hl { get; set; } = "en";

    /// <summary>
    ///     Country code used
    /// </summary>
    [JsonPropertyName("gl")]
    public string Gl { get; set; } = "us";
}
