using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsReviewDTO
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("rating")]
    public int? Rating { get; set; }

    [JsonPropertyName("contributor_id")]
    public string? ContributorId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("images")]
    public List<MapsImageDTO>? Images { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}
