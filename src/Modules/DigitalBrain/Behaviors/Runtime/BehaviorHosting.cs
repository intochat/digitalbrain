using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

public static class BehaviorHosting
{
    public static IServiceCollection AddBehavior<T>(this IServiceCollection services,
        Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>>? requirements = null) where T : class, IBehavior
    {
        services.TryAddSingleton<BehaviorReadiness>();
        services.AddHealthChecks().AddCheck<BehaviorReadiness>("behavior-subscriptions");
        services.AddSingleton(new BehaviorRequirements<T>(requirements ?? (_ => [])));
        services.AddHostedService<BehaviorHost<T>>();
        return services;
    }
}

internal sealed record BehaviorRequirements<T>(Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>> Resolve);

internal sealed class BehaviorHost<T>(IServiceProvider services, IDigitalBrain brain,
    BehaviorReadiness readiness, BehaviorRequirements<T> requirements,
    IHostApplicationLifetime lifetime, ILogger<BehaviorHost<T>> logger) : BackgroundService
    where T : class, IBehavior
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var required = requirements.Resolve(brain);
        var generation = readiness.Begin(typeof(T).FullName!, required);
        readiness.End(generation);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedReg = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var stoppingReg = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        if (lifetime.ApplicationStarted.IsCancellationRequested)
        {
            started.TrySetResult();
        }

        try
        {
            await started.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            generation = readiness.Begin(typeof(T).FullName!, required);
            await using var scopedBrain = new BehaviorScopedBrain(brain, readiness, generation);
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                var behavior = ActivatorUtilities.CreateInstance<T>(services, scopedBrain);
                var run = behavior.RunAsync(execution.Token);
                var loss = readiness.WaitForLossAsync(generation);
                if (await Task.WhenAny(run, loss).ConfigureAwait(false) == loss)
                {
                    await execution.CancelAsync().ConfigureAwait(false);
                    try { await run.WaitAsync(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                    throw new InvalidOperationException("A required behavior subscription closed.");
                }
                await run.ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
                lifetime.StopApplication();
                throw;
            }
            catch (Exception error)
            {
                readiness.End(generation);
                logger.LogError(error, "Behavior {Behavior} failed; readiness is withdrawn before retry.", typeof(T).Name);
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
            }
            finally { readiness.End(generation); }
        }
    }
}
