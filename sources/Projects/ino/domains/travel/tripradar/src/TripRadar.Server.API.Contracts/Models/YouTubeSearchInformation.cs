using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class YouTubeSearchInformation
{
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("video_results_state")]
    public string? VideoResultsState { get; set; }
}
