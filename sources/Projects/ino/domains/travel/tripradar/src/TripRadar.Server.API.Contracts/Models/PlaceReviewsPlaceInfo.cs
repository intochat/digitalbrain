using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsPlaceInfo
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("data_cid")] public string? DataCid { get; set; }

    [JsonPropertyName("reviews_link")] public string? ReviewsLink { get; set; }

    [JsonPropertyName("photos_link")] public string? PhotosLink { get; set; }

    [JsonPropertyName("gps_coordinates")] public GpsCoordinates? GpsCoordinates { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("reviews_id")] public string? ReviewsId { get; set; }

    [JsonPropertyName("located_in")] public string? LocatedIn { get; set; }

    [JsonPropertyName("rating")] public double? Rating { get; set; }

    [JsonPropertyName("reviews")] public int? Reviews { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("types")] public List<string>? Types { get; set; }

    [JsonPropertyName("address")] public string? Address { get; set; }

    [JsonPropertyName("open_state")] public string? OpenState { get; set; }

    [JsonPropertyName("hours")] public string? Hours { get; set; }

    [JsonPropertyName("operating_hours")] public PlaceReviewsOperatingHours? OperatingHours { get; set; }

    [JsonPropertyName("phone")] public string? Phone { get; set; }

    [JsonPropertyName("website")] public string? Website { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("price")] public string? Price { get; set; }

    [JsonPropertyName("editorial_summary")]
    public PlaceReviewsEditorialSummary? EditorialSummary { get; set; }

    [JsonPropertyName("user_review")] public PlaceReviewsUserReview? UserReview { get; set; }
}
