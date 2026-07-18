using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class LocalPagination
{
    [JsonPropertyName("current")] public int Current { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }

    [JsonPropertyName("other_pages")] public Dictionary<string, string>? OtherPages { get; set; }
}
