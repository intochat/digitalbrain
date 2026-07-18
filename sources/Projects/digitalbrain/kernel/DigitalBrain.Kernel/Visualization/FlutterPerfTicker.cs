using Microsoft.Extensions.Options;

namespace DigitalBrain.Kernel.Visualization;

internal sealed class FlutterPerfTicker(
    IGrainFactory grains,
    IOptions<FlutterPerfOptions> options,
    ILogger<FlutterPerfTicker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.TickInterval);
        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var neuron = grains.GetGrain<IFlutterPerfNeuron>(Guid.Empty);
                await neuron.Tick();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FlutterPerf tick failed.");
            }
        }
    }
}
