using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Registry;

internal sealed class RegistryObserver(LocalSignalHub signals, IGrainFactory grains, ILogger<RegistryObserver> logger) : BackgroundService
{
    private readonly LocalSignalHub.Subscription<NeuronActivity> _subscription = signals.Subscribe<NeuronActivity>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var activity in _subscription.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            // Recording our own deactivation would reactivate us forever.
            if (activity.TypeIds.Contains("registry", StringComparer.Ordinal)) { continue; }
            try { await grains.GetGrain<IRegistryObserver>(RegistryModule.Key).Observe(activity).WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            { logger.LogWarning(error, "Could not record runtime observation for {NeuronId}.", activity.NeuronId); }
        }
    }

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }
}