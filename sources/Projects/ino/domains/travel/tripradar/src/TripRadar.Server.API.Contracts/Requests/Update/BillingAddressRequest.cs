using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class BillingAddressRequest
{
    [JsonPropertyName("country")]
    [DataMember(Name = "country")]
    [Obfuscated]
    public string? Country { get; set; }

    [JsonPropertyName("postalCode")]
    [DataMember(Name = "postalCode")]
    [Obfuscated]
    public string? PostalCode { get; set; }
}
