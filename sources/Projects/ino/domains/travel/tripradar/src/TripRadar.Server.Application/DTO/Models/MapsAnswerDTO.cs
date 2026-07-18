using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsAnswerDTO
{
    [JsonPropertyName("user")]
    public MapsUserDTO? User { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}
