using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("data_id")] public string? DataId { get; set; }

    [JsonPropertyName("sort_by")] public string? SortBy { get; set; }

    [JsonPropertyName("topic_id")] public string? TopicId { get; set; }

    [JsonPropertyName("hl")] public string? Hl { get; set; }

    [JsonPropertyName("num")] public int? Num { get; set; }

    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
}
