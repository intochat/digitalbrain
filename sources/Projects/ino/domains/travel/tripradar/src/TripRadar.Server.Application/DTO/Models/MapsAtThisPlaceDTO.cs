using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsAtThisPlaceDTO
{
    [JsonPropertyName("type")]
    public List<MapsPlaceTypeDTO>? Type { get; set; }

    [JsonPropertyName("places")]
    public List<MapsSubPlaceDTO>? Places { get; set; }
}
