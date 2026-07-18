using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class ActivateUserResponse
{
    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; set; }

    [JsonPropertyName("refreshToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("username")]
    public required string Username { get; set; }
}
