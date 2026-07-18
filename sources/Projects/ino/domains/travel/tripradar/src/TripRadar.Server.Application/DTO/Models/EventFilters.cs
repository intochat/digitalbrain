using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class EventFilters
{
    [JsonPropertyName("htichips")]
    public List<string>? Htichips { get; set; }
}
