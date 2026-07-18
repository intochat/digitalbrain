using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalPaginationDTO
{
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("other_pages")] public Dictionary<string, string>? OtherPages { get; set; }
}
