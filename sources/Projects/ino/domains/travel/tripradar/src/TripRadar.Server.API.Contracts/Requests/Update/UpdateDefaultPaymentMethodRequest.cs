using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class UpdateDefaultPaymentMethodRequest
{
    [JsonPropertyName("brand")]
    [DataMember(Name = "brand")]
    [Obfuscated]
    public string? Brand { get; set; }

    [JsonPropertyName("last4")]
    [DataMember(Name = "last4")]
    [Obfuscated]
    public string Last4 { get; set; } = null!;

    [JsonPropertyName("expMonth")]
    [DataMember(Name = "expMonth")]
    [Obfuscated]
    public int ExpMonth { get; set; }

    [JsonPropertyName("expYear")]
    [DataMember(Name = "expYear")]
    [Obfuscated]
    public int ExpYear { get; set; }

    [JsonPropertyName("setAsDefault")]
    [DataMember(Name = "setAsDefault")]
    public bool SetAsDefault { get; set; } = true;

    [JsonPropertyName("billingDetails")]
    [DataMember(Name = "billingDetails")]
    public BillingDetailsRequest? BillingDetails { get; set; }
}
