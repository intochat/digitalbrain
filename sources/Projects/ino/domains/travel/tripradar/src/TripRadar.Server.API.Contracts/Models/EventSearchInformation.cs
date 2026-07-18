using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class EventSearchInformation
{
    [JsonPropertyName("events_results_state")] public string? EventsResultsState { get; set; }
}
