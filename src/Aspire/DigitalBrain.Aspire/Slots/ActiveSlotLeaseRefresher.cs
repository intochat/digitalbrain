using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Aspire;

// The lease row is bootstrapped by an Orleans startup task below BecomeActive (see
// DigitalBrainRuntimeHostingExtensions.ConfigureActiveSlotLease), before the silo serves any grain call;
// this hosted service only keeps the cache fresh afterward. An unread verdict — before the startup task or
// the first tick has run — reads as standby (HoldsLease false), the safe default.
internal sealed class ActiveSlotLeaseRefresher(AzureTableActiveSlotLease lease, ILogger<ActiveSlotLeaseRefresher> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;
    private bool _stopped;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop = RefreshAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            if (_loop is { } loop)
            {
                try
                {
                    await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The host's shutdown timeout elapsed before the loop noticed cancellation; nothing more to wait for.
                    logger.LogDebug("Active-slot lease refresher did not stop before the shutdown timeout.");
                }
            }
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private async Task RefreshAsync()
    {
        // The one interval both sides of a promotion agree on (Task 1).
        using var timer = new PeriodicTimer(ActiveSlotNames.RefreshInterval);
        while (await WaitForTickOrStopAsync(timer).ConfigureAwait(false))
        {
            try
            {
                await lease.RefreshAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // a storage hiccup must not tear the silo down; the cached verdict stands until the next read
            catch (Exception error)
#pragma warning restore CA1031
            {
                logger.LogWarning(error, "Re-reading the active-slot lease failed; slot '{Slot}' keeps its last verdict.", lease.Slot);
            }
        }
    }

    private async Task<bool> WaitForTickOrStopAsync(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
