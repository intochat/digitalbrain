namespace TripRadar.Server.Infrastructure.Settings;

/// <summary>
/// Configuration for resilience policies used by external service providers.
/// Can be configured via appsettings.json under "ResiliencePolicy" section.
/// </summary>
public class ResiliencePolicySettings
{
    /// <summary>
    /// Number of retry attempts before giving up. Default: 3
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Number of consecutive failures before opening the circuit breaker. Default: 5
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open. Default: 60 (1 minute)
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout in seconds for each individual request. Default: 15
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    public TimeSpan CircuitBreakerDuration => TimeSpan.FromSeconds(CircuitBreakerDurationSeconds);
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}
