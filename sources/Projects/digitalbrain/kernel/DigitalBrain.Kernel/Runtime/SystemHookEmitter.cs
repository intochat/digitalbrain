using DigitalBrain.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class SystemHookEmitter(
    SynapseBroadcaster broadcaster,
    IGrainFactory grains,
    ILogger<SystemHookEmitter> logger) : IHostedService
{
    int _brainStartedFired;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await grains.GetGrain<IBrainCatalog>("global")
            .ListRegisteredAsync();

        var empty = (IReadOnlyDictionary<string, string>)
            new Dictionary<string, string>(StringComparer.Ordinal);
        await broadcaster.BroadcastSystemSignalAsync(
            "DigitalBrain.Kernel.Loaded", empty, cancellationToken);

        logger.LogInformation(
            "Emitted system hook DigitalBrain.Kernel.Loaded.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task EmitBrainStartedIfFirstAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _brainStartedFired, 1, 0) != 0)
            return;

        var empty = (IReadOnlyDictionary<string, string>)
            new Dictionary<string, string>(StringComparer.Ordinal);
        await broadcaster.BroadcastSystemSignalAsync(
            "DigitalBrain.Brain.Started", empty, ct);
        logger.LogInformation("Emitted system hook DigitalBrain.Brain.Started.");
    }
}
