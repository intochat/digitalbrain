using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsAdmissionDTO
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("options")]
    public List<MapsAdmissionOptionDTO>? Options { get; set; }
}
