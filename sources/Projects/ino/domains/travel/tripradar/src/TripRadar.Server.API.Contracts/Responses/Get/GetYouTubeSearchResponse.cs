using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetYouTubeSearchResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public YouTubeSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public YouTubeSearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("video_results")]
    public List<Dictionary<string, object>>? VideoResults { get; set; }

    [JsonPropertyName("playlist_results")]
    public List<Dictionary<string, object>>? PlaylistResults { get; set; }

    [JsonPropertyName("channel_results")]
    public List<Dictionary<string, object>>? ChannelResults { get; set; }

    [JsonPropertyName("movie_results")]
    public List<Dictionary<string, object>>? MovieResults { get; set; }

    [JsonPropertyName("ads_results")]
    public List<Dictionary<string, object>>? AdsResults { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpapiPagination { get; set; }

    [JsonPropertyName("pagination")]
    public Dictionary<string, object>? Pagination { get; set; }
}
