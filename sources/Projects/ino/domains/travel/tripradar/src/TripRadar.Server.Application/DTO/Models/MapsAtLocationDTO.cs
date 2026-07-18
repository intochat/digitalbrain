using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Convertors;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsAtLocationDTO
{
    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("data_id")]
    public string? DataId { get; set; }

    [JsonPropertyName("data_cid")]
    public string? DataCid { get; set; }

    [JsonPropertyName("reviews_link")]
    public string? ReviewsLink { get; set; }

    [JsonPropertyName("photos_link")]
    public string? PhotosLink { get; set; }

    [JsonPropertyName("gps_coordinates")]
    public GpsCoordinatesDTO? GpsCoordinates { get; set; }

    [JsonPropertyName("place_id_search")]
    public string? PlaceIdSearch { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }
}
