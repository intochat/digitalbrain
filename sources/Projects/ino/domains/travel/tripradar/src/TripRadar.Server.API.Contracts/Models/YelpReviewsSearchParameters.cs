using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class YelpReviewsSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("yelp_domain")] public string? YelpDomain { get; set; }

    [JsonPropertyName("language")] public string? Language { get; set; }

    [JsonPropertyName("sortby")] public string? SortBy { get; set; }

    [JsonPropertyName("rating")] public int? Rating { get; set; }

    [JsonPropertyName("not_recommended")] public bool? NotRecommended { get; set; }

    [JsonPropertyName("start")] public int? Start { get; set; }

    [JsonPropertyName("num")] public int? Num { get; set; }

    [JsonPropertyName("q")] public string? Q { get; set; }
}
