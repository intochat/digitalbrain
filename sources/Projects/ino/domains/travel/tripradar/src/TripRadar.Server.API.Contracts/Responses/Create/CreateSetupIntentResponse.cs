using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreateSetupIntentResponse
{
    [JsonPropertyName("clientSecret")]
    [DataMember(Name = "clientSecret")]
    public string? ClientSecret { get; set; }
}
