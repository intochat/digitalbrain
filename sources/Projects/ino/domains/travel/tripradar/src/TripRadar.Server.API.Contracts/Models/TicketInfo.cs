using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class TicketInfo
{
    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("link_type")] public string? LinkType { get; set; }
}
