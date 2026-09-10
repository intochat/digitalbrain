namespace DigitalBrain.Google;

internal sealed record GmailTokenGrant(string AccessToken, string? RefreshToken, string? GrantedScopes, int ExpiresInSeconds);
