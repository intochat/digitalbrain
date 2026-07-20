namespace DigitalBrain.Abstractions;

public abstract record ConnectionHealth;

public sealed record ConnectionHealthy : ConnectionHealth;

public sealed record ConnectionMissingAppCredentials(string Detail) : ConnectionHealth;

public sealed record ConnectionNotConfigured : ConnectionHealth;

public sealed record ConnectionNotAuthorized : ConnectionHealth;

public sealed record ConnectionTokenExpired : ConnectionHealth;

public sealed record ConnectionProviderError(string Detail) : ConnectionHealth;

public sealed record ConnectionNetworkError(string Detail) : ConnectionHealth;

public interface IConnectionProviderAdapter
{
    string Provider { get; }

    Task<ConnectionHealth> ExchangeAuthorizationCodeAsync(
        string code,
        CancellationToken cancellationToken);

    Task<ConnectionHealth> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}

public enum ConnectionLifecycleState
{
    NotConfigured = 0,
    Authorizing = 1,
    Authorized = 2,
    Expired = 3,
    Revoked = 4,
}
