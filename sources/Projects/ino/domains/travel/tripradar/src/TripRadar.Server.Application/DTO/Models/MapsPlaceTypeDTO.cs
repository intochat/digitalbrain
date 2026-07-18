using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPlaceTypeDTO
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("places")]
    public int? Places { get; set; }
}
