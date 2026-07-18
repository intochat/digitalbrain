using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Payment method details from Stripe.
/// </summary>
public class StripePaymentMethodInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("brand")]
    public string Brand { get; set; } = null!;

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("last4")]
    public string Last4 { get; set; } = null!;

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("expMonth")]
    public int ExpMonth { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("expYear")]
    public int ExpYear { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("addressCountry")]
    public string? AddressCountry { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("addressPostalCode")]
    public string? AddressPostalCode { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
