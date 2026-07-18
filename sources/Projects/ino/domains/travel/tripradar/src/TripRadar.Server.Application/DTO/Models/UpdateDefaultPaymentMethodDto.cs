using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class UpdateDefaultPaymentMethodDto
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("defaultPaymentMethodLast4")]
    public string? DefaultPaymentMethodLast4 { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("defaultPaymentMethodExpMonth")]
    public int? DefaultPaymentMethodExpMonth { get; set; }

    [Comms.Core.Attributes.Obfuscated]
    [JsonPropertyName("defaultPaymentMethodExpYear")]
    public int? DefaultPaymentMethodExpYear { get; set; }
}
