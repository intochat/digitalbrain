using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class YouTubeSearchInformationDTO
{
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("video_results_state")]
    public string? VideoResultsState { get; set; }
}
