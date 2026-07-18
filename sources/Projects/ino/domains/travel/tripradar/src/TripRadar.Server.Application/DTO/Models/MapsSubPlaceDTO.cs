using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Convertors;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsSubPlaceDTO
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

    [JsonPropertyName("reviews")]
    public int? Reviews { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("type_id")]
    public string? TypeId { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("open_state")]
    public string? OpenState { get; set; }

    [JsonPropertyName("hours")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Hours { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }
}
