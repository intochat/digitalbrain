namespace Brain.Modules.Connections;

public interface IConnectionProvider
{
    string BuildAuthorizationUrl(string state);
    Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct);
}

public sealed record ConnectionToken(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public sealed record ProbeResult(string Health, string Detail);

public static class ConnectionHealth
{
    public const string Healthy = "healthy";
    public const string MissingAppCredentials = "missingAppCredentials";
    public const string NotConfigured = "notConfigured";
    public const string NotAuthorized = "notAuthorized";
    public const string TokenExpired = "tokenExpired";
    public const string ProviderError = "providerError";
    public const string NetworkError = "networkError";
}
