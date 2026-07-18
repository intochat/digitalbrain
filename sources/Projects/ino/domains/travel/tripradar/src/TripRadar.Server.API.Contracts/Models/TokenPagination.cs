using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class TokenPagination
{
    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
}
