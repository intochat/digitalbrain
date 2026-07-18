using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsUser
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("contributor_id")] public string? ContributorId { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }

    [JsonPropertyName("local_guide")] public bool? LocalGuide { get; set; }

    [JsonPropertyName("reviews")] public int? Reviews { get; set; }

    [JsonPropertyName("photos")] public int? Photos { get; set; }
}
