namespace TripRadar.Bot.Auth;

public sealed record UserSession(
    string Username,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);
