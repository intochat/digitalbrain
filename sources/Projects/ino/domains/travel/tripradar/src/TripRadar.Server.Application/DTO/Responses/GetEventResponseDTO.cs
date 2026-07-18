using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetEventResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public EventSearchParametersDTO? SearchParameters { get; set; }

    [JsonPropertyName("search_information")]
    public EventSearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("events_results")]
    public List<EventDTO>? EventsResults { get; set; }
}
