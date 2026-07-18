using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Responses.Update;

public sealed class UpdateDefaultPaymentMethodResponse
{
    [JsonPropertyName("message")]
    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("defaultPaymentMethodLast4")]
    [DataMember(Name = "defaultPaymentMethodLast4")]
    [Obfuscated]
    public string? DefaultPaymentMethodLast4 { get; set; }

    [JsonPropertyName("defaultPaymentMethodExpMonth")]
    [DataMember(Name = "defaultPaymentMethodExpMonth")]
    [Obfuscated]
    public int? DefaultPaymentMethodExpMonth { get; set; }

    [JsonPropertyName("defaultPaymentMethodExpYear")]
    [DataMember(Name = "defaultPaymentMethodExpYear")]
    [Obfuscated]
    public int? DefaultPaymentMethodExpYear { get; set; }
}
