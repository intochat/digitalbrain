using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Update;

public sealed class ToggleSubscriptionResponse
{
    [JsonPropertyName("message")]
    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string Status { get; set; } = string.Empty;
}
