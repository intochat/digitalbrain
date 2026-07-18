using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateRefreshTokenRequest
{
    [JsonPropertyName("refreshToken")]
    [DataMember(Name = "refreshToken")]
    [Obfuscated]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessToken")]
    [DataMember(Name = "accessToken")]
    [Obfuscated]
    public string? AccessToken { get; set; }
}
