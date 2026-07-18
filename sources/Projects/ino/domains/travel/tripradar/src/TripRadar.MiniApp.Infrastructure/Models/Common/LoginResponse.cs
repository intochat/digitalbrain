using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common;

public sealed record LoginResponse(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken
);