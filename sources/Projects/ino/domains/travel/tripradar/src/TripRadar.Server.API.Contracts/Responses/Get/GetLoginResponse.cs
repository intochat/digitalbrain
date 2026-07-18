using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetLoginResponse
{
    [JsonPropertyName("token")] public required string? Token { get; set; }

    [JsonPropertyName("refreshToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }
}
