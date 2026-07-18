using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class SearchQuery
{
    [JsonPropertyName("q")]
    public string Q { get; set; } = null!; // Query
}
