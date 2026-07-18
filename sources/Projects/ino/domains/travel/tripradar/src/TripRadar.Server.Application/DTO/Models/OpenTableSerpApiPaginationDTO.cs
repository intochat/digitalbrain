using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class OpenTableSerpApiPaginationDTO
{
    [JsonPropertyName("previous")] public string? Previous { get; set; }

    [JsonPropertyName("next")] public string? Next { get; set; }
}
