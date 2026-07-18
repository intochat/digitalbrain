using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class EventSearchInformation
{
    [JsonPropertyName("events_results_state")]
    public string? EventsResultsState { get; set; }
}
