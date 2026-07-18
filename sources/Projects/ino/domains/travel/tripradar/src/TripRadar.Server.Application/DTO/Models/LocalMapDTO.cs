using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class LocalMapDTO
{
    [JsonPropertyName("image")]
    public string? Image { get; set; }
}
