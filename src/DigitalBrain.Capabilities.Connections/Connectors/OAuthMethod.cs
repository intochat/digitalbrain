namespace DigitalBrain.Capabilities.Connections.Connectors;

// One browser-authorized method of a connector. The client id and secret live in the named
// Integration; the Platform owns the redirect, the callback, the code exchange and refresh.
public sealed record OAuthMethod(
    string Id,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string[] Scopes,
    string IntegrationId,
    string ClientIdField = "ClientId",
    string ClientSecretField = "ClientSecret",
    bool Pkce = true)
{
    // Extra query parameters on the authorization URL (Google's access_type=offline).
    public Dictionary<string, string> AuthorizationParameters { get; init; } = new(StringComparer.Ordinal);
    public OAuthExternalOperations? ExternalOperations { get; init; }
}

public sealed record OAuthExternalOperations(string CodeExchange, string Refresh);

public interface IOAuthTokenExchange
{
    Task<OAuthGrant> ExchangeCodeAsync(string connectionKey, string authMethodId, string code, string codeVerifier, string redirectUri, string? target, CancellationToken ct);
    Task<OAuthGrant> RefreshAsync(string connectionKey, string authMethodId, string refreshToken, CancellationToken ct);
}

public enum OAuthTokenFailure { ReconsentRequired, Unavailable, InvalidResponse }

public sealed class OAuthTokenExchangeException(OAuthTokenFailure failure) : InvalidOperationException(failure switch
{
    OAuthTokenFailure.ReconsentRequired => "The connector refused the grant. Connect the account again.",
    OAuthTokenFailure.InvalidResponse => "The connector returned an invalid token response.",
    _ => "The token endpoint is unavailable. Try again later.",
})
{
    public OAuthTokenFailure Failure { get; } = failure;
}
