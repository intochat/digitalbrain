using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

public static class ConnectionLifecycle
{
    public static ConnectionLifecycleState Advance(
        ConnectionLifecycleState current,
        ConnectionHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        return health switch
        {
            ConnectionHealthy => ConnectionLifecycleState.Authorized,
            ConnectionMissingAppCredentials => ConnectionLifecycleState.NotConfigured,
            ConnectionNotConfigured => ConnectionLifecycleState.NotConfigured,
            ConnectionNotAuthorized => ConnectionLifecycleState.Authorizing,
            ConnectionTokenExpired => ConnectionLifecycleState.Expired,
            ConnectionProviderError => current == ConnectionLifecycleState.Authorized
                ? ConnectionLifecycleState.Authorized
                : current,
            ConnectionNetworkError => current,
            _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Connection health is a closed union."),
        };
    }

    public static string DescribeAction(ConnectionHealth health) => health switch
    {
        ConnectionHealthy => "none",
        ConnectionMissingAppCredentials => "administrator-must-configure",
        ConnectionNotConfigured => "administrator-must-configure",
        ConnectionNotAuthorized => "user-must-sign-in",
        ConnectionTokenExpired => "refresh-or-sign-in",
        ConnectionProviderError => "retry",
        ConnectionNetworkError => "retry",
        _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Connection health is a closed union."),
    };
}
