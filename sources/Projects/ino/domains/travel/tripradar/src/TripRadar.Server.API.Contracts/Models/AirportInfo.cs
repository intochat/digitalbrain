using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class AirportInfo
{
    [JsonPropertyName("departure")] public List<AirportDetail>? Departure { get; set; }

    [JsonPropertyName("arrival")] public List<AirportDetail>? Arrival { get; set; }
}
