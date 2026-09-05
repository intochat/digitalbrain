using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Scripting.Startup;

internal sealed class DigitalBrainBehaviorAdmissionSource(
    IDigitalBrain brain,
    IGrainFactory grains,
    ILogger<DigitalBrainBehaviorAdmissionSource>? logger = null) : IBehaviorAdmissionSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    public async IAsyncEnumerable<IReadOnlyList<BehaviorDefinition>> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var behaviors = brain.Get<IBehaviors>();
        var query = grains.GetGrain<IBehaviorsKernel>(behaviors.Id.ToGrainId());
        using var changed = new SemaphoreSlim(0, 1);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var notifications = WatchChangesAsync(behaviors, changed, watching.Token);
        try
        {
            // Definitions are authoritative. Notifications reduce latency; the periodic
            // refresh repairs missed journal rows and observers lost after silo restart.
            while (true)
            {
                yield return await ReadCurrentAsync(query, cancellationToken).ConfigureAwait(false);
                await changed.WaitAsync(RefreshInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await watching.CancelAsync().ConfigureAwait(false);
            try
            {
                await notifications.WaitAsync(RefreshInterval).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The transport's Unwatch can be awaiting an unavailable silo. Its
                // cleanup may finish later; it must not block this host's shutdown.
                logger?.LogWarning("Behavior journal cleanup is still waiting for the silo");
            }
        }
    }

    public Task ReportAsync(ReportBehaviorStatus report, CancellationToken cancellationToken)
        => brain.Get<IBehaviors>().SendAsync(report, cancellationToken);

    private async Task<IReadOnlyList<BehaviorDefinition>> ReadCurrentAsync(
        IBehaviorsKernel query, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await query.ReadCurrent().WaitAsync(RefreshInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Current behaviors are unavailable; retrying after reconnect");
                await Task.Delay(RefreshInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WatchChangesAsync(
        NeuronReference<IBehaviors> behaviors, SemaphoreSlim changed, CancellationToken cancellationToken)
    {
        var cursor = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (var page in behaviors.WatchJournalAsync(JournalKind.Outgoing, cursor, cancellationToken))
                {
                    cursor = page.ResumeSequence;
                    if ((page.ResetSnapshot is not null || page.Delta.Any(
                        delivery => delivery.Signal is BehaviorAdmitted or BehaviorRemoved)) && changed.CurrentCount == 0)
                    {
                        changed.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                logger?.LogWarning(exception, "Behavior journal disconnected; snapshots continue to reconcile");
            }

            try
            {
                await Task.Delay(RefreshInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
