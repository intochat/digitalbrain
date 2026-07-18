using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Net;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Polly v8 resilience pipeline registered as a named
/// <see cref="HttpClient"/> per LLM provider. Provider factories opt in by
/// pulling their named client from <see cref="IHttpClientFactory"/> in
/// <see cref="ILlmProviderFactory.CreateClient"/>; calls then ride the same
/// retry / circuit breaker / timeout pipeline regardless of which SDK the
/// factory wraps. Mirrors IAW's
/// <c>Aspire.Client/LlmResilienceConfiguration.cs</c>.
/// </summary>
public static class LlmResilienceConfiguration
{
    /// <summary>
    /// Registers a resilient named <see cref="HttpClient"/> for each given
    /// provider. Safe to call once per silo at startup with the union of
    /// declared providers.
    /// </summary>
    public static IHostApplicationBuilder AddInoLlmResilience(
        this IHostApplicationBuilder builder,
        IEnumerable<string> providers)
    {
        foreach (var provider in providers.Distinct(StringComparer.OrdinalIgnoreCase))
            ConfigureProvider(builder, provider);
        return builder;
    }

    static void ConfigureProvider(IHostApplicationBuilder builder, string providerName)
    {
        builder.Services.AddHttpClient(providerName)
            .AddResilienceHandler($"{providerName}-llm", pipeline =>
            {
                pipeline.AddConcurrencyLimiter(permitLimit: 5, queueLimit: 10);

                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 4,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests
                                        || r.StatusCode >= HttpStatusCode.InternalServerError),
                    DelayGenerator = static args =>
                    {
                        // Honor the provider's Retry-After when supplied — better
                        // than blind exponential backoff during a real rate limit.
                        if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } retryAfter)
                            return new ValueTask<TimeSpan?>(retryAfter + TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800)));
                        return new ValueTask<TimeSpan?>(default(TimeSpan?));
                    }
                });

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    MinimumThroughput = 5,
                    FailureRatio = 0.8,
                    SamplingDuration = TimeSpan.FromSeconds(15),
                    BreakDuration = TimeSpan.FromSeconds(10),
                });

                pipeline.AddTimeout(TimeSpan.FromSeconds(60));
            });
    }
}
