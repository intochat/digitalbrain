using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Delete;

/// <summary>
/// Response returned after successfully deleting a payment method.
/// </summary>
public class DeletePaymentMethodResponse
{
    /// <summary>
    /// Success message.
    /// </summary>
    [JsonPropertyName("message")]
    [DataMember(Name = "message")]
    public string Message { get; set; } = "Payment method removed successfully";

    /// <summary>
    /// Last 4 digits of the new default payment method (if assigned).
    /// </summary>
    [JsonPropertyName("newDefaultPaymentMethodLast4")]
    [DataMember(Name = "newDefaultPaymentMethodLast4")]
    public string? NewDefaultPaymentMethodLast4 { get; set; }

    /// <summary>
    /// Expiration month of the new default payment method (if assigned).
    /// </summary>
    [JsonPropertyName("newDefaultPaymentMethodExpMonth")]
    [DataMember(Name = "newDefaultPaymentMethodExpMonth")]
    public int? NewDefaultPaymentMethodExpMonth { get; set; }

    /// <summary>
    /// Expiration year of the new default payment method (if assigned).
    /// </summary>
    [JsonPropertyName("newDefaultPaymentMethodExpYear")]
    [DataMember(Name = "newDefaultPaymentMethodExpYear")]
    public int? NewDefaultPaymentMethodExpYear { get; set; }

    /// <summary>
    /// Number of remaining payment methods after deletion.
    /// </summary>
    [JsonPropertyName("remainingPaymentMethods")]
    [DataMember(Name = "remainingPaymentMethods")]
    public int RemainingPaymentMethods { get; set; }
}
