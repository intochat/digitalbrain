using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscriptionId")]
    [DataMember(Name = "subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("clientSecret")]
    [DataMember(Name = "clientSecret")]
    public string? ClientSecret { get; set; }
}
