using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MultiCityLeg
{
    [JsonPropertyName("departure_id")]
    public required string DepartureId { get; set; }

    [JsonPropertyName("arrival_id")]
    public required string ArrivalId { get; set; }

    [JsonPropertyName("date")]
    public required string Date { get; set; }

    [JsonPropertyName("times")]
    public required string Times { get; set; }
}
