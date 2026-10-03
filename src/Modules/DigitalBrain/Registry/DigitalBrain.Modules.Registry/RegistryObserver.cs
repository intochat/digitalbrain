using DigitalBrain.Contracts.Signals;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Registry;

internal sealed class RegistryObserver(LocalSignalHub signals, IGrainFactory grains, ILogger<RegistryObserver> logger) : BackgroundService
{
    private readonly LocalSignalHub.Subscription<NeuronActivity> _subscription = signals.Subscribe<NeuronActivity>();

    // Activations arrive in bursts (every neuron an intent touches); coalescing them into one
    // grain call per window keeps registry bookkeeping out of each intent's span budget.
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);
    private const int MaxBatch = 256;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<NeuronActivity>(MaxBatch);
        while (await _subscription.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            await Task.Delay(CoalesceWindow, stoppingToken).ConfigureAwait(false);
            batch.Clear();
            while (batch.Count < MaxBatch && _subscription.Reader.TryRead(out var activity))
            {
                // Recording our own deactivation would reactivate us forever.
                if (activity.TypeIds.Contains("registry", StringComparer.Ordinal)) { continue; }
                batch.Add(activity);
            }
            if (batch.Count == 0) { continue; }
            try { await grains.GetGrain<IRegistryObserver>(RegistryModule.Key).ObserveBatch([.. batch]).WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            { logger.LogWarning(error, "Could not record a batch of {Count} runtime observations.", batch.Count); }
        }
    }

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }
}
