using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class EventDTO
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("date")]
    public EventDateDTO Date { get; set; } = null!;

    [JsonPropertyName("address")]
    public List<string> Address { get; set; } = null!;

    [JsonPropertyName("link")]
    public string Link { get; set; } = null!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("ticket_info")]
    public List<TicketInfoDTO> TicketInfo { get; set; } = null!;

    [JsonPropertyName("venue")]
    public VenueDTO? Venue { get; set; }

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = null!;

    [JsonPropertyName("event_location_map")]
    public EventLocationMapDTO? EventLocationMap { get; set; }
}
