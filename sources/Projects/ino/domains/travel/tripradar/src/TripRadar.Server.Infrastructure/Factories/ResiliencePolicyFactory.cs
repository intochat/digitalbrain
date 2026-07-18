using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Factories;

/// <summary>
/// Factory for creating standardized resilience policies for external service providers.
/// Centralizes retry, circuit breaker, and timeout policies to ensure consistent behavior.
/// </summary>
public static class ResiliencePolicyFactory
{
    /// <summary>
    /// Creates a standard resilience policy with retry, circuit breaker, and timeout.
    /// </summary>
    public static IAsyncPolicy<TResult> CreateStandardPolicy<TResult>(
        string providerName,
        ILogger logger,
        ResiliencePolicySettings? settings = null)
    {
        settings ??= new ResiliencePolicySettings();

        var retryPolicy = CreateRetryPolicy<TResult>(providerName, logger, settings.RetryCount);
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy<TResult>(providerName, logger, settings.CircuitBreakerThreshold, settings.CircuitBreakerDuration);
        var timeoutPolicy = Policy.TimeoutAsync<TResult>(settings.Timeout);

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy, timeoutPolicy);
    }

    /// <summary>
    /// Creates a standard resilience policy without a specific result type.
    /// </summary>
    public static IAsyncPolicy CreateStandardPolicy(
        string providerName,
        ILogger logger,
        ResiliencePolicySettings? settings = null)
    {
        settings ??= new ResiliencePolicySettings();

        var retryPolicy = CreateRetryPolicy(providerName, logger, settings.RetryCount);
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy(providerName, logger, settings.CircuitBreakerThreshold, settings.CircuitBreakerDuration);
        var timeoutPolicy = Policy.TimeoutAsync(settings.Timeout);

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy, timeoutPolicy);
    }

    private static IAsyncPolicy<TResult> CreateRetryPolicy<TResult>(string providerName, ILogger logger, int retryCount) =>
        Policy<TResult>
            .Handle<Exception>(ex => ex is not TimeoutRejectedException)
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (outcome, delay, attempt, _) =>
                {
                    if (outcome.Exception != null)
                    {
                        logger.LogWarning(
                            outcome.Exception,
                            "{ProviderName} retry {Attempt} after {Delay}",
                            providerName, attempt, delay);
                    }
                });

    private static IAsyncPolicy CreateRetryPolicy(string providerName, ILogger logger, int retryCount) =>
        Policy
            .Handle<Exception>(ex => ex is not TimeoutRejectedException)
            .WaitAndRetryAsync(
                retryCount,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (exception, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        exception,
                        "{ProviderName} retry {Attempt} after {Delay}",
                        providerName, attempt, delay);
                });

    private static IAsyncPolicy<TResult> CreateCircuitBreakerPolicy<TResult>(
        string providerName,
        ILogger logger,
        int threshold,
        TimeSpan duration) =>
        Policy<TResult>
            .Handle<Exception>()
            .CircuitBreakerAsync(
                threshold,
                duration,
                (outcome, breakDuration) =>
                {
                    if (outcome.Exception != null)
                    {
                        logger.LogWarning(
                            outcome.Exception,
                            "{ProviderName} circuit breaker open for {Duration}",
                            providerName, breakDuration);
                    }
                },
                () => logger.LogInformation("{ProviderName} circuit breaker reset", providerName));

    private static IAsyncPolicy CreateCircuitBreakerPolicy(
        string providerName,
        ILogger logger,
        int threshold,
        TimeSpan duration) =>
        Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                threshold,
                duration,
                (exception, breakDuration) =>
                {
                    logger.LogWarning(
                        exception,
                        "{ProviderName} circuit breaker opened for {Duration}",
                        providerName, breakDuration);
                },
                () => logger.LogInformation("{ProviderName} circuit breaker reset", providerName));
}
