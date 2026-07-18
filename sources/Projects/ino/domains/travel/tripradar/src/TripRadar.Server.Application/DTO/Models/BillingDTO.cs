using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Billing details DTO.
/// </summary>
public class BillingDTO
{
    [JsonPropertyName("name")]
    [Obfuscated]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    [Obfuscated]
    public string? Email { get; set; }

    [JsonPropertyName("address")]
    public AddressDTO? Address { get; set; }
}
