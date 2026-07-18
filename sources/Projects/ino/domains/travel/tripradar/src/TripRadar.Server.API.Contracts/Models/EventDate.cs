using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class EventDate
{
    [JsonPropertyName("start_date")] public string? StartDate { get; set; }

    [JsonPropertyName("when")] public string? When { get; set; }
}
