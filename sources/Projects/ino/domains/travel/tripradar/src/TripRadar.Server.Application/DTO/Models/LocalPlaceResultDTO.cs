using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Convertors;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalPlaceResultDTO
{
    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("reviews_original")]
    public string? ReviewsOriginal { get; set; }

    [JsonPropertyName("reviews")]
    public int? Reviews { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("place_id")]
    public string PlaceId { get; set; } = string.Empty;

    [JsonPropertyName("place_id_search")]
    public string? PlaceIdSearch { get; set; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    [JsonPropertyName("lsig")]
    public string? Lsig { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("gps_coordinates")]
    public GpsCoordinatesDTO? GpsCoordinates { get; set; }

    [JsonPropertyName("service_options")]
    public ServiceOptionsDTO? ServiceOptions { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("hours")]
    public string? Hours { get; set; }

    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }

    [JsonPropertyName("links")]
    public PlaceLinksDTO? Links { get; set; }
}
