using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class TicketInfoDTO
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = null!;

    [JsonPropertyName("link")]
    public string Link { get; set; } = null!;

    [JsonPropertyName("linkType")]
    public string LinkType { get; set; } = null!;
}
