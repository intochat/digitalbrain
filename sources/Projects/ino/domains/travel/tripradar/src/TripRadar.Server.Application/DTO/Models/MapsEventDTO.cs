using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsEventDTO
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("date")]
    public MapsEventDateDTO? Date { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("ticket_info")]
    public MapsTicketInfoDTO? TicketInfo { get; set; }
}
