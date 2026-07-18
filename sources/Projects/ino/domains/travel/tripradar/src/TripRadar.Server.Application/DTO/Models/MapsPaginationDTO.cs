using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPaginationDTO
{
    [JsonPropertyName("start")]
    public int? Start { get; set; }
    [JsonPropertyName("num")]
    public int? Num { get; set; }
}
