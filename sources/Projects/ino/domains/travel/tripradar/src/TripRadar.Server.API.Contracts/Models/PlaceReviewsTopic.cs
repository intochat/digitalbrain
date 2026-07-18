using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsTopic
{
    [JsonPropertyName("keyword")] public string? Keyword { get; set; }

    [JsonPropertyName("mentions")] public int Mentions { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }
}
