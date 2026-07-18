using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class EventLocationMapDTO
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = null!;

    [JsonPropertyName("link")]
    public string Link { get; set; } = null!;

    [JsonPropertyName("serpapi_link")]
    public string SerpapiLink { get; set; } = null!;
}
