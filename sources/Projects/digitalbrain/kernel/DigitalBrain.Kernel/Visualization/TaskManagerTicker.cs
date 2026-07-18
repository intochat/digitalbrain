using Microsoft.Extensions.Options;

namespace DigitalBrain.Kernel.Visualization;

// Drives the TaskManager projection on a fixed cadence. The neuron grain is
// addressed as the cluster-singleton (Guid.Empty) — same key TimelineRelayGrain
// uses — so the ticker, the observer, and the implicit-stream subscriber all
// converge on one activation. The first tick also forces the observer grain
// to materialize so its implicit subscription is wired before edges arrive.
internal sealed class TaskManagerTicker(
    IGrainFactory grains,
    IOptions<TaskManagerOptions> options,
    ILogger<TaskManagerTicker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var observer = grains.GetGrain<ITaskManagerObserverGrain>(Guid.Empty);
            await observer.EnsureActivatedAsync();
            logger.LogInformation("Task manager observer activated.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to pre-activate TaskManager observer; it will activate on first message.");
        }

        using var timer = new PeriodicTimer(options.Value.TickInterval);
        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var neuron = grains.GetGrain<ITaskManagerNeuron>(Guid.Empty);
                await neuron.Tick();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TaskManager tick failed.");
            }
        }
    }
}
