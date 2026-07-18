using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Event
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("date")] public EventDate? Date { get; set; }

    [JsonPropertyName("address")] public List<string>? Address { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("ticket_info")] public List<TicketInfo>? TicketInfo { get; set; }

    [JsonPropertyName("venue")] public Venue? Venue { get; set; }

    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }

    [JsonPropertyName("event_location_map")]
    public EventLocationMap? EventLocationMap { get; set; }
}
