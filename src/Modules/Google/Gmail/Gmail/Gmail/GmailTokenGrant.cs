namespace DigitalBrain.Google.Gmail;

internal sealed record GmailTokenGrant(
    string AccessToken,
    string? RefreshToken,
    string? GrantedScopes,
    int ExpiresInSeconds,
    string? Email = null);