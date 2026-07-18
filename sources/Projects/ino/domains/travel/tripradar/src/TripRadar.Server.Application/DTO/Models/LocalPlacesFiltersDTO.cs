using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalPlacesFiltersDTO
{
    [JsonPropertyName("tbs")]
    public string? Tbs { get; set; }
}
