using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record RefreshTokenRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("accessToken")] string? AccessToken = null
    );
}