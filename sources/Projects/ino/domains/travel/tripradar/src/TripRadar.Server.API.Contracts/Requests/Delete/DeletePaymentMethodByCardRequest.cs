using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Delete;

/// <summary>
/// Request to delete a payment method by card details shown to the user (last4 + expiry + optional brand).
/// </summary>
public class DeletePaymentMethodByCardRequest
{
    /// <summary>
    /// Card brand (e.g., Visa, Mastercard). Optional but recommended to avoid ambiguous matches.
    /// </summary>
    [JsonPropertyName("brand")]
    [DataMember(Name = "brand")]
    [Obfuscated]
    public string? Brand { get; set; }

    /// <summary>
    /// Last 4 digits of the card.
    /// </summary>
    [Required]
    [JsonPropertyName("last4")]
    [DataMember(Name = "last4")]
    [Obfuscated]
    public string Last4 { get; set; } = null!;

    /// <summary>
    /// Expiration month (1-12).
    /// </summary>
    [Range(1, 12)]
    [JsonPropertyName("expMonth")]
    [DataMember(Name = "expMonth")]
    [Obfuscated]
    public int ExpMonth { get; set; }

    /// <summary>
    /// Expiration year (e.g., 2028).
    /// </summary>
    [Range(2000, 2100)]
    [JsonPropertyName("expYear")]
    [DataMember(Name = "expYear")]
    [Obfuscated]
    public int ExpYear { get; set; }
}
