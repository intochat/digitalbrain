using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetEventsResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public EventSearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("search_information")]
    public EventSearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("events_results")] public List<Event>? EventsResults { get; set; }
}