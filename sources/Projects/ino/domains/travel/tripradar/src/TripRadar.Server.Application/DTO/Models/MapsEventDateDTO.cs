using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsEventDateDTO
{
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("when")]
    public string? When { get; set; }
}
