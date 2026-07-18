using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsUserReviewsDTO
{
    [JsonPropertyName("summary")]
    public List<MapsReviewSummaryDTO>? Summary { get; set; }

    [JsonPropertyName("most_relevant")]
    public List<MapsReviewDTO>? MostRelevant { get; set; }
}
