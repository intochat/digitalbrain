using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Address DTO.
/// </summary>
public class AddressDTO
{
    [JsonPropertyName("country")]
    [Obfuscated]
    public string? Country { get; set; }

    [JsonPropertyName("postalCode")]
    [Obfuscated]
    public string? PostalCode { get; set; }
}
