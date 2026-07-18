using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class DiscoverMorePlaceDTO
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("serp_api_link")]
    public string? SerpApiLink { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("places")]
    public string? Places { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }
}
