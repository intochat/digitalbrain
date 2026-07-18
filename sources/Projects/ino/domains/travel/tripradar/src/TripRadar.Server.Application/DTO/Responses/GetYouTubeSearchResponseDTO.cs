using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetYouTubeSearchResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public YouTubeSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public YouTubeSearchInformationDTO? SearchInformation { get; set; }

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
