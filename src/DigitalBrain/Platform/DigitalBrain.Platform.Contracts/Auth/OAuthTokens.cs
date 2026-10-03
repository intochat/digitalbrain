namespace DigitalBrain.Platform.Contracts.Auth;

public sealed record OAuthTokens(string AccessToken, string? RefreshToken);
