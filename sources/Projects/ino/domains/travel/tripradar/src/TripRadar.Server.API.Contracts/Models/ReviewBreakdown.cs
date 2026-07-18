using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class ReviewBreakdown
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("total_mentioned")] public int TotalMentioned { get; set; }

    [JsonPropertyName("positive")] public int Positive { get; set; }

    [JsonPropertyName("negative")] public int Negative { get; set; }

    [JsonPropertyName("neutral")] public int Neutral { get; set; }
}
