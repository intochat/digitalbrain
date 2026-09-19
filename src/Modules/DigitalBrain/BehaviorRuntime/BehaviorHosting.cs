using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Core;

public static class BehaviorHosting
{
    public static IServiceCollection AddBehavior<T>(this IServiceCollection services) where T : class, IBehavior
    {
        services.AddSingleton<T>();
        services.AddHostedService<BehaviorHost<T>>();
        return services;
    }
}

internal sealed class BehaviorHost<T>(T behavior, IHostApplicationLifetime lifetime) : BackgroundService
    where T : class, IBehavior
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            try
            {
                await behavior.RunAsync(stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
