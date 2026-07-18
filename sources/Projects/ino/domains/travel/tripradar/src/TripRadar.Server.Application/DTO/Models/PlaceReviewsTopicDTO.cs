using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsTopicDTO
{
    [JsonPropertyName("keyword")]
    public string? Keyword { get; set; }

    [JsonPropertyName("mentions")]
    public int Mentions { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
