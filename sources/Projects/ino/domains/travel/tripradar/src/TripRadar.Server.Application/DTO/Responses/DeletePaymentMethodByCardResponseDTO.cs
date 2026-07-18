using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Responses;

public sealed class DeletePaymentMethodByCardResponseDTO
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("newDefaultPaymentMethodLast4")]
    public string? NewDefaultPaymentMethodLast4 { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("newDefaultPaymentMethodExpMonth")]
    public int? NewDefaultPaymentMethodExpMonth { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("newDefaultPaymentMethodExpYear")]
    public int? NewDefaultPaymentMethodExpYear { get; set; }

    [JsonPropertyName("remainingPaymentMethods")]
    public int RemainingPaymentMethods { get; set; }
}
