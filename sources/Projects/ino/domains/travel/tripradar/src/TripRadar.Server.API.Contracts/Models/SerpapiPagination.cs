using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class SerpapiPagination
{
    [JsonPropertyName("current_from")] public int CurrentFrom { get; set; }

    [JsonPropertyName("current_to")] public int CurrentTo { get; set; }

    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }
}
