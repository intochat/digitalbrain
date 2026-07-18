using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class OpenTableSerpApiPagination
{
    [JsonPropertyName("previous")] public string? Previous { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }
}
