using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class BillingDetailsRequest
{
    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Obfuscated]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    [Obfuscated]
    public string? Email { get; set; }

    [JsonPropertyName("address")]
    [DataMember(Name = "address")]
    public BillingAddressRequest? Address { get; set; }
}
