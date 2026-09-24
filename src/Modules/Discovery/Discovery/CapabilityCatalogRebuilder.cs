using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Discovery;

// Warm the catalog at startup, then rebuild only when a manifest-change signal invalidates it.
// A failed warmup or a lost change feed keeps keyword search available and never stops the host.
internal sealed class CapabilityCatalogRebuilder(
    CapabilityCatalog catalog,
    IDigitalBrain brain,
    ILogger<CapabilityCatalogRebuilder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmAsync(stoppingToken).ConfigureAwait(false);
        try
        {
            var directory = brain.Get<IAppManifestDirectory>(AppManifestDirectoryGrains.Key);
            await using var changes = await brain.SubscribeAsync<AppCatalogued>(directory, stoppingToken).ConfigureAwait(false);
            await foreach (var _ in changes.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                catalog.Invalidate();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // a lost change feed must not stop the silo; the index rebuilds on the next search
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Capability catalog change feed stopped; discovery retries on the next search.");
        }
#pragma warning restore CA1031
    }

    private async Task WarmAsync(CancellationToken stoppingToken)
    {
        try
        {
            await catalog.RebuildAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // a failed warmup must not stop the silo; search retries lazily
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Initial capability catalog rebuild failed; discovery will retry on first search.");
        }
#pragma warning restore CA1031
    }
}
