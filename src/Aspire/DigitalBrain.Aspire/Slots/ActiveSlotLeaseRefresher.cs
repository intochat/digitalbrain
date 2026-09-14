using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Aspire;

// HoldsLease is read on every grain call, so the row is read out of band. The first read happens before
// the host serves anything: a live slot must not be fenced while it boots.
internal sealed class ActiveSlotLeaseRefresher(AzureTableActiveSlotLease lease, ILogger<ActiveSlotLeaseRefresher> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lease.BootstrapAsync(cancellationToken).ConfigureAwait(false);
        _loop = RefreshAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _stopping.Dispose();
    }

    private async Task RefreshAsync()
    {
        // The one interval both sides of a promotion agree on (Task 1).
        using var timer = new PeriodicTimer(ActiveSlotNames.RefreshInterval);
        while (await SafeWaitAsync(timer).ConfigureAwait(false))
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

    private async Task<bool> SafeWaitAsync(PeriodicTimer timer)
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
