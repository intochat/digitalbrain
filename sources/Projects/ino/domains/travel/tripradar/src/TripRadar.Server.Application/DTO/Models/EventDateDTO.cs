using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class EventDateDTO
{
    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = null!;

    [JsonPropertyName("when")]
    public string When { get; set; } = null!;
}
