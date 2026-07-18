using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalAdvertisementResultDTO
{
    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("ad_title")]
    public string? AdTitle { get; set; }

    [JsonPropertyName("displayed_link")]
    public string? DisplayedLink { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("reviews_original")]
    public string? ReviewsOriginal { get; set; }

    [JsonPropertyName("reviews")]
    public int? Reviews { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("hours")]
    public string? Hours { get; set; }

    [JsonPropertyName("place_id")]
    public string PlaceId { get; set; } = string.Empty;

    [JsonPropertyName("place_id_search")]
    public string? PlaceIdSearch { get; set; }

    [JsonPropertyName("lsig")]
    public string? Lsig { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("gps_coordinates")]
    public GpsCoordinatesDTO? GpsCoordinates { get; set; }

    [JsonPropertyName("service_options")]
    public ServiceOptionsDTO? ServiceOptions { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }
}
