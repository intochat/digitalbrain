using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.Application.DTO.Models;

public class CardDTO
{
    [JsonPropertyName("brand")]
    public string Brand { get; set; } = null!;

    [JsonPropertyName("last4")]
    [Obfuscated]
    public string Last4 { get; set; } = null!;

    [JsonPropertyName("expMonth")]
    [Obfuscated]
    public int ExpMonth { get; set; }

    [JsonPropertyName("expYear")]
    [Obfuscated]
    public int ExpYear { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
