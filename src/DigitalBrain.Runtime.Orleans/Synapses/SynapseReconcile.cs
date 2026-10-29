using DigitalBrain.Runtime.Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// Silo-start reconcile (04 D-04, D-12; contract "Write-then-crash window"): a signal appended before a crash may never have been
/// announced, and an inactive subscriber is woken by nothing else. After the silo becomes active, every brain listed in
/// <see cref="ISynapseRegistryIndex"/> is checked: for each publisher in the brain's <see cref="ISynapseCursorRegistry"/>, the
/// subscribers <c>Behind</c> its last sequence get <see cref="ISynapseHost.CatchUp"/>, at most <see cref="MaxConcurrentCatchUps"/>
/// at a time. Brains come only from the cluster-stored index; no local folder is listed. Running it twice, on one silo or on
/// two, is harmless: catch-up is cursor-deduped.
/// </summary>
internal sealed partial class SynapseReconcile(IServiceProvider services, ILogger<SynapseReconcile> log) : ILifecycleParticipant<ISiloLifecycle>, IDisposable
{
    /// <summary>The bound on concurrent catch-up calls (T-04-39).</summary>
    public const int MaxConcurrentCatchUps = 64;

    private readonly CancellationTokenSource _stopping = new();

    /// <summary>The run started with the silo; facts may await it.</summary>
    public Task Started { get; private set; } = Task.CompletedTask;

    public void Participate(ISiloLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        lifecycle.Subscribe(nameof(SynapseReconcile), ServiceLifecycleStage.Active, Start, Stop);
    }

    private Task Start(CancellationToken ct)
    {
        // In the background: a slow brain must not hold silo start.
        Started = Task.Run(() => Run(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task Stop(CancellationToken ct)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        try { await Started.WaitAsync(ct).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException) { }
    }

    public void Dispose() => _stopping.Dispose();

    /// <summary>One reconcile pass over every indexed brain.</summary>
    public async Task Run(CancellationToken ct)
    {
        try
        {
            if (services.GetService<BrainSignalHistories>() is not { } histories) { return; } // no BrainLog, no synapses
            var grains = services.GetRequiredService<IGrainFactory>();
            var types = services.GetRequiredService<NeuronTypeRegistry>();
            var behind = new HashSet<string>(StringComparer.Ordinal);
            foreach (var brain in await grains.GetGrain<ISynapseRegistryIndex>(SynapseRegistryIndex.Key).List().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var registry = grains.GetGrain<ISynapseCursorRegistry>(brain);
                    foreach (var publisher in await registry.Publishers().ConfigureAwait(false))
                    {
                        var last = (await histories.For(publisher.Brain).Read(publisher, 0, 1, ct).ConfigureAwait(false)).Last;
                        behind.UnionWith(await registry.Behind(publisher, last).ConfigureAwait(false));
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException) { LogBrainFailed(log, error, brain); }
            }
            await Parallel.ForEachAsync(behind, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentCatchUps, CancellationToken = ct }, async (subscriber, _) =>
            {
                if (!NeuronId.TryParse(subscriber, out var id) || !types.TryGet(id.Type, out var neuronClass)) { return; }
                var grainType = NeuronTypeRegistry.GrainTypeOf(NeuronTypeRegistry.PolicyOf(neuronClass));
                try { await grains.GetGrain<ISynapseHost>(GrainId.Create(grainType, subscriber)).CatchUp().ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { LogSubscriberFailed(log, error, subscriber); }
            }).ConfigureAwait(false);
            LogReconciled(log, behind.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException) { LogFailed(log, error); }
    }

    [LoggerMessage(EventId = 4130, EventName = "SynapseReconciled", Level = LogLevel.Information, Message = "Synapse reconcile woke {Count} subscribers behind their publishers.")]
    private static partial void LogReconciled(ILogger logger, int count);

    [LoggerMessage(EventId = 4131, EventName = "SynapseReconcileBrainFailed", Level = LogLevel.Warning, Message = "Synapse reconcile of brain {Brain} failed.")]
    private static partial void LogBrainFailed(ILogger logger, Exception error, string brain);

    [LoggerMessage(EventId = 4132, EventName = "SynapseReconcileSubscriberFailed", Level = LogLevel.Warning, Message = "Synapse reconcile could not wake {Subscriber}.")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception error, string subscriber);

    [LoggerMessage(EventId = 4133, EventName = "SynapseReconcileFailed", Level = LogLevel.Warning, Message = "Synapse reconcile failed.")]
    private static partial void LogFailed(ILogger logger, Exception error);
}
