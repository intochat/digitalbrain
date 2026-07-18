using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class AuthenticationModel
{
    [JsonPropertyName("token")]
    public required string? Token { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }
}
