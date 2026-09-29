using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Registry;

internal sealed class RegistryObserver(RuntimeSignals signals, IGrainFactory grains, ILogger<RegistryObserver> logger) : BackgroundService
{
    private readonly RuntimeSignals.Subscription _subscription = signals.Subscribe();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in _subscription.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            // Recording our own deactivation would reactivate us forever.
            if (signal is not NeuronActivity activity || activity.TypeIds.Contains("registry", StringComparer.Ordinal)) { continue; }
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